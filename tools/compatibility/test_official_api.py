import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

from official_api import additions_report, changed_types

DIFF = '''## Mafi.Core
```diff
  namespace Example {
-   public class Gone {
-     public void Old();
-   }
    public class Outer {
      public class Nested {
-       public int Value;
+       public string Value;
      }
    }
  }
  namespace Other {
+   public class Added {
+     public void New();
+   }
  }
```
'''


class OfficialApiTests(unittest.TestCase):
    def test_removed_added_and_nested_scopes(self):
        records = changed_types(DIFF)
        self.assertIn('Example.Gone', records)
        self.assertIn('Other.Added', records)
        self.assertEqual(records['Example.Outer.Nested']['removed'], ['public int Value;'])
        self.assertEqual(records['Example.Outer.Nested']['added'], ['public string Value;'])

    def test_opportunity_index_keeps_unused_additions_and_replacement_context(self):
        report = additions_report(changed_types(DIFF), [
            {'project': 'AutoTerrainDesignations', 'candidates': ['Example.Outer.Nested']}])
        # A current-usage filter would lose this new capability entirely.
        self.assertIn('Other.Added', report)
        self.assertIn('public void New();', report)
        self.assertNotIn('Example.Gone', report)
        # Preserve both signatures so a replacement is not mistaken for a new feature.
        self.assertIn('public int Value;', report)
        self.assertIn('public string Value;', report)

    def run_import(self, content, source='class Added {}'):
        with tempfile.TemporaryDirectory(prefix='coi-api-test-') as directory:
            root = Path(directory)
            local = root / 'source.md'
            local.write_text(content, encoding='utf-8')
            src = root / 'AutoTerrainDesignations' / 'src'
            src.mkdir(parents=True)
            (src / 'Use.cs').write_text(source, encoding='utf-8')
            before = (src / 'Use.cs').read_bytes()
            output = root / 'report'
            result = subprocess.run([sys.executable, str(Path(__file__).with_name('official_api.py')),
                                     '--release', '4.3', '--workspace', str(root), '--input', str(local),
                                     '--output', str(output)], capture_output=True, text=True)
            self.assertEqual((src / 'Use.cs').read_bytes(), before)
            metadata = json.loads((output / 'official-api-source.json').read_text())
            impact = json.loads((output / 'official-api-impact.json').read_text()) if result.returncode == 0 else None
            self.assertEqual((output / 'official-api-additions.md').exists(), result.returncode == 0)
            return result.returncode, metadata, impact

    def test_import_is_hashed_and_correlated_without_source_edits(self):
        code, metadata, impact = self.run_import(DIFF)
        self.assertEqual(code, 0)
        self.assertEqual(metadata['method'], 'local-import')
        self.assertEqual(len(metadata['sha256']), 64)
        self.assertEqual(impact['sourceLeads'][0]['candidates'], ['Other.Added'])

    def test_html_error_is_not_a_clean_report(self):
        code, metadata, _ = self.run_import('<html>Sign in</html>')
        self.assertEqual(code, 2)
        self.assertEqual(metadata['status'], 'unavailable')


if __name__ == '__main__':
    unittest.main()
