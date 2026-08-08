// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
//
// Unofficial mod for Captain of Industry. Captain of Industry, MaFi Games, and
// related trademarks, code, and assets belong to MaFi Games. This repository
// is intended to contain only original mod code/configuration; if MaFi Games
// material is included by mistake, I intend to correct it promptly upon
// discovery or notice.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace AutoTerrainDesignations
{
    /// <summary>
    /// Provides the Planar icon from embedded SVG markup.
    ///
    /// The game can render its catalogued SVG assets, but does not expose a
    /// runtime loader for mod-relative SVG files. The markup is therefore
    /// parsed and rasterized once into a tintable Sprite, following the same
    /// runtime-generated icon pattern used by AFD.
    /// </summary>
    internal static class PlanarCornerIcon
    {
        private const int RenderSize = 128;
        private const float ViewBoxSize = 32f;

        private const string SvgMarkup =
            "<svg viewBox=\"0 0 32 32\" xmlns=\"http://www.w3.org/2000/svg\">" +
            "<path d=\"M3.85 2.25H28.15Q29.75 2.25 29.75 3.85V28.15Q29.75 29.75 28.15 29.75H3.85Q2.25 29.75 2.25 28.15V3.85Q2.25 2.25 3.85 2.25Z\" fill=\"none\" stroke=\"#fff\" stroke-width=\"2.25\"/>" +
            "<path d=\"M6.5 25.5L13.32 18.68\" fill=\"none\" stroke=\"#fff\" stroke-width=\"2.25\" stroke-linecap=\"butt\"/>" +
            "<path d=\"M13.32 24.34L13.32 18.68L7.66 18.68\" fill=\"none\" stroke=\"#fff\" stroke-width=\"2.25\" stroke-linecap=\"butt\"/>" +
            "<path d=\"M13.32 17.555Q14.445 17.555 14.445 18.68Q14.445 19.805 13.32 19.805Q12.195 19.805 12.195 18.68Q12.195 17.555 13.32 17.555Z\" fill=\"#fff\"/>" +
            "<path d=\"M17.5 14.5L24.32 7.68\" fill=\"none\" stroke=\"#fff\" stroke-width=\"2.25\" stroke-linecap=\"butt\"/>" +
            "<path d=\"M24.32 13.34L24.32 7.68L18.66 7.68\" fill=\"none\" stroke=\"#fff\" stroke-width=\"2.25\" stroke-linecap=\"butt\"/>" +
            "<path d=\"M24.32 6.555Q25.445 6.555 25.445 7.68Q25.445 8.805 24.32 8.805Q23.195 8.805 23.195 7.68Q23.195 6.555 24.32 6.555Z\" fill=\"#fff\"/>" +
            "</svg>";

        private static readonly Regex PathTokenRegex = new Regex(
            @"[MLHVQZ]|[-+]?(?:\d*\.\d+|\d+\.?\d*)(?:[eE][-+]?\d+)?",
            RegexOptions.Compiled);

        private static Sprite? s_sprite;

        internal static void Install(VisualElement element)
        {
            element.style.backgroundImage = new StyleBackground(GetSprite());
            element.MarkDirtyRepaint();
        }

        private static Sprite GetSprite()
        {
            if (s_sprite != null)
                return s_sprite;

            Texture2D texture = RasterizeSvg(SvgMarkup);
            texture.name = "ATD_PlanarCornerIconTexture";
            texture.filterMode = FilterMode.Bilinear;

            s_sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, RenderSize, RenderSize),
                new Vector2(0.5f, 0.5f),
                RenderSize);
            s_sprite.name = "ATD_PlanarCornerIcon";
            return s_sprite;
        }

        private static Texture2D RasterizeSvg(string markup)
        {
            var document = XDocument.Parse(markup);
            var shapes = new List<SvgShape>();

            foreach (XElement path in document.Root!.Elements())
            {
                string? data = (string?)path.Attribute("d");
                if (string.IsNullOrEmpty(data))
                    continue;

                var shape = new SvgShape(ParsePath(data!));
                string fill = ((string?)path.Attribute("fill") ?? "none").ToLowerInvariant();
                string stroke = ((string?)path.Attribute("stroke") ?? "none").ToLowerInvariant();
                shape.Filled = fill != "none";
                shape.Stroked = stroke != "none";
                shape.RoundCaps =
                    ((string?)path.Attribute("stroke-linecap") ?? "butt")
                    .Equals("round", StringComparison.OrdinalIgnoreCase);
                shape.StrokeWidth = ParseFloat(path.Attribute("stroke-width"), 0f);
                shapes.Add(shape);
            }

            var pixels = new Color[RenderSize * RenderSize];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = Color.clear;

            for (int pixelY = 0; pixelY < RenderSize; pixelY++)
            {
                for (int pixelX = 0; pixelX < RenderSize; pixelX++)
                {
                    Vector2 point = new Vector2(
                        (pixelX + 0.5f) * ViewBoxSize / RenderSize,
                        (pixelY + 0.5f) * ViewBoxSize / RenderSize);

                    bool covered = false;
                    foreach (SvgShape shape in shapes)
                    {
                        if (shape.Filled && IsInside(point, shape.Points))
                        {
                            covered = true;
                            break;
                        }

                        if (shape.Stroked && IsOnStroke(
                            point,
                            shape.Points,
                            shape.Closed,
                            shape.RoundCaps,
                            shape.StrokeWidth))
                        {
                            covered = true;
                            break;
                        }
                    }

                    if (covered)
                    {
                        int textureY = RenderSize - 1 - pixelY;
                        pixels[textureY * RenderSize + pixelX] = Color.white;
                    }
                }
            }

            var texture = new Texture2D(
                RenderSize,
                RenderSize,
                TextureFormat.RGBA32,
                mipChain: false);
            texture.SetPixels(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            return texture;
        }

        private static List<Vector2> ParsePath(string data)
        {
            var points = new List<Vector2>();
            MatchCollection tokens = PathTokenRegex.Matches(data);
            char command = '\0';
            int index = 0;
            float x = 0f;
            float y = 0f;
            float startX = 0f;
            float startY = 0f;

            while (index < tokens.Count)
            {
                string token = tokens[index].Value;
                if (token.Length == 1 && char.IsLetter(token[0]))
                {
                    command = token[0];
                    index++;
                    if (command == 'Z')
                    {
                        if (points.Count > 0 && points[points.Count - 1] != new Vector2(startX, startY))
                            points.Add(new Vector2(startX, startY));
                        x = startX;
                        y = startY;
                    }
                    continue;
                }

                switch (command)
                {
                    case 'M':
                        x = Number(tokens, ref index);
                        y = Number(tokens, ref index);
                        points.Add(new Vector2(x, y));
                        startX = x;
                        startY = y;
                        command = 'L';
                        break;
                    case 'L':
                        x = Number(tokens, ref index);
                        y = Number(tokens, ref index);
                        points.Add(new Vector2(x, y));
                        break;
                    case 'H':
                        x = Number(tokens, ref index);
                        points.Add(new Vector2(x, y));
                        break;
                    case 'V':
                        y = Number(tokens, ref index);
                        points.Add(new Vector2(x, y));
                        break;
                    case 'Q':
                        float controlX = Number(tokens, ref index);
                        float controlY = Number(tokens, ref index);
                        float endX = Number(tokens, ref index);
                        float endY = Number(tokens, ref index);
                        Vector2 start = new Vector2(x, y);
                        for (int step = 1; step <= 6; step++)
                        {
                            float t = step / 6f;
                            float inverse = 1f - t;
                            points.Add(new Vector2(
                                inverse * inverse * start.x
                                    + 2f * inverse * t * controlX
                                    + t * t * endX,
                                inverse * inverse * start.y
                                    + 2f * inverse * t * controlY
                                    + t * t * endY));
                        }
                        x = endX;
                        y = endY;
                        break;
                    default:
                        index++;
                        break;
                }
            }

            return points;
        }

        private static float Number(MatchCollection tokens, ref int index)
        {
            if (index >= tokens.Count)
                return 0f;

            return float.Parse(
                tokens[index++].Value,
                CultureInfo.InvariantCulture);
        }

        private static float ParseFloat(XAttribute? attribute, float fallback)
        {
            if (attribute == null)
                return fallback;

            return float.TryParse(
                attribute.Value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float value)
                ? value
                : fallback;
        }

        private static bool IsInside(Vector2 point, List<Vector2> polygon)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[j];
                bool crosses = (a.y > point.y) != (b.y > point.y);
                if (crosses && point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }

            return inside;
        }

        private static bool IsOnStroke(
            Vector2 point,
            List<Vector2> points,
            bool closed,
            bool roundCaps,
            float width)
        {
            if (points.Count < 2)
                return false;

            float halfWidth = width * 0.5f;
            int segmentCount = closed ? points.Count : points.Count - 1;
            for (int i = 0; i < segmentCount; i++)
            {
                Vector2 a = points[i];
                Vector2 b = points[(i + 1) % points.Count];
                Vector2 delta = b - a;
                float denominator = Vector2.Dot(delta, delta);
                float t = denominator > 0f
                    ? Vector2.Dot(point - a, delta) / denominator
                    : 0f;
                if (!roundCaps && (t < 0f || t > 1f))
                    continue;

                t = Mathf.Clamp01(t);
                if (Vector2.Distance(point, a + delta * t) <= halfWidth)
                    return true;
            }

            return false;
        }

        private sealed class SvgShape
        {
            internal readonly List<Vector2> Points;
            internal bool Filled;
            internal bool Stroked;
            internal bool RoundCaps;
            internal float StrokeWidth;

            internal bool Closed => Points.Count > 2 && Points[0] == Points[Points.Count - 1];

            internal SvgShape(List<Vector2> points)
            {
                Points = points;
            }
        }
    }
}
