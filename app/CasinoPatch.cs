using Mono.Cecil;
using Mono.Cecil.Cil;

namespace LethalModpackUpdater;

public static class CasinoPatch
{
    public const string Id = "casino-v81-hud";

    public static void Apply(string originalPath, string outputPath)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(originalPath);
        var roulette = assembly.MainModule.GetType("LethalCasino.Custom.Roulette")
            ?? throw new InvalidDataException("Tipo da roleta não encontrado.");
        var methods = roulette.Methods.Where(method => method.Name == "UpdateLocalPlayerBetHUD").ToList();
        if (methods.Count != 1) throw new InvalidDataException("Método da roleta não encontrado exatamente uma vez.");
        var instructions = methods[0].Body.Instructions;
        var fields = Enumerable.Range(0, instructions.Count)
            .Where(index => instructions[index].Operand?.ToString() == "UnityEngine.UI.Image HUDManager::loadingDarkenScreen")
            .ToList();
        if (fields.Count != 1) throw new InvalidDataException("Campo antigo da roleta não encontrado exatamente uma vez.");
        var first = fields[0] - 1;
        var expected = new[]
        {
            "HUDManager HUDManager::get_Instance()",
            "UnityEngine.UI.Image HUDManager::loadingDarkenScreen",
            "",
            "System.Void UnityEngine.Behaviour::set_enabled(System.Boolean)"
        };
        if (first < 0 || first + expected.Length > instructions.Count ||
            instructions[fields[0] + 1].OpCode.Name != "ldc.i4.0")
            throw new InvalidDataException("Sequência antiga da roleta não corresponde à versão examinada.");
        for (var offset = 0; offset < expected.Length; offset++)
            if ((instructions[first + offset].Operand?.ToString() ?? "") != expected[offset])
                throw new InvalidDataException("Sequência antiga da roleta não corresponde à versão examinada.");
        for (var offset = 0; offset < expected.Length; offset++)
        {
            instructions[first + offset].OpCode = OpCodes.Nop;
            instructions[first + offset].Operand = null;
        }
        assembly.Write(outputPath);
    }
}
