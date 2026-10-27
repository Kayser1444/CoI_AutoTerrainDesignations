using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Mono.Cecil;
using CecilInstruction = Mono.Cecil.Cil.Instruction;
using CecilVariable = Mono.Cecil.Cil.VariableDefinition;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            string managed = Path.GetFullPath(args[0]);
            AppDomain.CurrentDomain.AssemblyResolve += (_, request) =>
            {
                string path = Path.Combine(managed, new AssemblyName(request.Name).Name + ".dll");
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };
            using var game = AssemblyDefinition.ReadAssembly(Path.Combine(managed, "Mafi.Unity.dll"));
            var type = game.MainModule.Types.Single(t => t.FullName == "Mafi.Unity.InputControl.GameMenu.Settings.ControlsTab");
            var method = type.Methods.Single(m => m.Name == "buildOwnerBlock");
            var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)!)
                .ToDictionary(o => o.Value);
            var native = method.Body.Instructions.Select(i => new CodeInstruction(opcodes[i.OpCode.Value], i.Operand)).ToList();
            var candidate = native;
            if (args.Length > 1 && args[1] != "--native")
            {
                var mod = Assembly.LoadFrom(Path.GetFullPath(args[1]));
                var injector = mod.GetType("CoI.AutoHelpers.InputControl.CustomKeybindsInjector", true)!;
                var transpiler = injector.GetMethod("ControlsLayout_Transpiler", BindingFlags.NonPublic | BindingFlags.Static)
                    ?? throw new InvalidOperationException("The layout patch is missing from this mod build.");
                candidate = ((IEnumerable<CodeInstruction>)transpiler.Invoke(null, new object[] { native })!).ToList();
            }
            var originalDecision = CompileDecision(native, method.Body.Variables.Count);
            var patchedDecision = CompileDecision(candidate, method.Body.Variables.Count);
            Check(!patchedDecision(1, 0), "One shortcut creates an empty left column before its first section.");
            Console.WriteLine("PASS: one shortcut stays in the first column.");
            for (int count = 2; count <= 50; count++)
                for (int placed = 0; placed <= count; placed++)
                    Check(originalDecision(count, placed) == patchedDecision(count, placed), "Multi-shortcut column balancing changed.");
            Console.WriteLine("PASS: native column decisions for 2..50 shortcuts are preserved.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    // Execute the real native threshold arithmetic and the branch leading to
    // newColumn(), without requiring Unity's visual tree or native UI calls.
    private static Func<int, int, bool> CompileDecision(List<CodeInstruction> code, int localCount)
    {
        int division = code.FindIndex(i => i.opcode == OpCodes.Div);
        Check(division >= 2 && code.Count(i => i.opcode == OpCodes.Div) == 1, "Native threshold division changed.");
        Check(code[division - 1].opcode == OpCodes.Ldc_I4_2, "Native threshold divisor changed.");
        int thresholdStore = code.FindIndex(division + 1, i => i.opcode == OpCodes.Stloc_S);
        int threshold = Local(code[thresholdStore]);
        int columns = Local(code[thresholdStore + 2]);
        int placed = Local(code[thresholdStore + 4]);
        int total = Local(code[division - 2]);
        int branch = code.FindIndex(thresholdStore + 5, i => i.opcode == OpCodes.Ldloc_S && Local(i) == columns);
        Check(branch >= 0 && code[branch + 2].opcode == OpCodes.Bne_Un &&
            code[branch + 5].opcode == OpCodes.Blt && Local(code[branch + 3]) == placed &&
            Local(code[branch + 4]) == threshold &&
            ReferenceEquals(code[branch + 2].operand, code[branch + 5].operand), "Native column creation branch changed.");
        var dynamic = new DynamicMethod("NativeControlsColumnDecision", typeof(bool), new[] { typeof(int), typeof(int) });
        var il = dynamic.GetILGenerator();
        var locals = Enumerable.Range(0, localCount).Select(_ => il.DeclareLocal(typeof(int))).ToArray();
        var noColumn = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, locals[total]);
        foreach (var instruction in code.Skip(division - 2).Take(thresholdStore + 5 - (division - 2))) Emit(instruction);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stloc, locals[placed]);
        foreach (var instruction in code.Skip(branch).Take(6)) Emit(instruction);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(noColumn);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        return (Func<int, int, bool>)dynamic.CreateDelegate(typeof(Func<int, int, bool>));

        void Emit(CodeInstruction instruction)
        {
            if (instruction.operand is CecilVariable variable) il.Emit(instruction.opcode, locals[variable.Index]);
            else if (instruction.operand is CecilInstruction) il.Emit(instruction.opcode, noColumn);
            else if (instruction.operand is MethodInfo target) il.Emit(instruction.opcode, target);
            else if (instruction.operand == null) il.Emit(instruction.opcode);
            else throw new InvalidOperationException("Unexpected native arithmetic operand.");
        }
    }

    private static int Local(CodeInstruction instruction) => instruction.operand is CecilVariable variable
        ? variable.Index : instruction.opcode == OpCodes.Ldloc_3 ? 3
        : throw new InvalidOperationException("Unexpected native local.");
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
