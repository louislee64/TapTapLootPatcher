using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length < 1)
{
    PrintUsage();
    return 1;
}

var command = args[0].Trim().ToLowerInvariant();

if (command == "simulate-high-tier")
{
    return SimulateHighTierDrops(args.Skip(1).ToArray());
}

if (args.Length < 2)
{
    Console.Error.WriteLine("This command requires an assembly path.");
    PrintUsage();
    return 1;
}

var assemblyPath = Path.GetFullPath(args[1]);

if (!File.Exists(assemblyPath))
{
    Console.Error.WriteLine($"Assembly not found: {assemblyPath}");
    return 1;
}

return command switch
{
    "scan-input" => ScanInput(assemblyPath),
    "scan-strings" => ScanStrings(assemblyPath, args.Skip(2).ToArray()),
    "list-types" => ListTypes(assemblyPath, args.Skip(2).ToArray()),
    "list-methods" => ListMethods(assemblyPath, args.Skip(2).ToArray()),
    "dump-method" => DumpMethod(assemblyPath, args.Skip(2).ToArray()),
    "dump-type" => DumpType(assemblyPath, args.Skip(2).ToArray()),
    "patch-autorun" => PatchAutorun(assemblyPath, args.Skip(2).ToArray()),
    "patch-mythic" => PatchMythic(assemblyPath, args.Skip(2).ToArray()),
    "patch-both" => PatchBoth(assemblyPath, args.Skip(2).ToArray()),
    "simulate-high-tier" => SimulateHighTierDrops(args.Skip(1).ToArray()),
    _ => UnknownCommand(command),
};

static void PrintUsage()
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  TapTapLootPatcher scan-input <assembly-path>");
    Console.Error.WriteLine("  TapTapLootPatcher scan-strings <assembly-path> <term>");
    Console.Error.WriteLine("  TapTapLootPatcher list-types <assembly-path> <term>");
    Console.Error.WriteLine("  TapTapLootPatcher list-methods <assembly-path> <term>");
    Console.Error.WriteLine("  TapTapLootPatcher dump-method <assembly-path> <term>");
    Console.Error.WriteLine("  TapTapLootPatcher dump-type <assembly-path> <term>");
    Console.Error.WriteLine("  TapTapLootPatcher patch-autorun <input-assembly> <output-assembly> [managed-dir]");
    Console.Error.WriteLine("  TapTapLootPatcher patch-mythic <input-assembly> <output-assembly> [managed-dir]");
    Console.Error.WriteLine("  TapTapLootPatcher patch-both <input-assembly> <output-assembly> [managed-dir]");
    Console.Error.WriteLine("  TapTapLootPatcher simulate-high-tier <count> [legendary-ratio-0-to-1]");
}

static int UnknownCommand(string command)
{
    Console.Error.WriteLine($"Unknown command: {command}");
    PrintUsage();
    return 1;
}

static DefaultAssemblyResolver BuildResolver(string assemblyPath, string? managedDir = null)
{
    var resolver = new DefaultAssemblyResolver();

    static void AddIfExists(DefaultAssemblyResolver r, string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
        {
            r.AddSearchDirectory(Path.GetFullPath(path));
        }
    }

    var assemblyDir = Path.GetDirectoryName(assemblyPath)!;
    AddIfExists(resolver, assemblyDir);
    AddIfExists(resolver, managedDir);

    if (Path.GetFileName(assemblyDir).Equals("Managed", StringComparison.OrdinalIgnoreCase))
    {
        AddIfExists(resolver, assemblyDir);
    }
    else
    {
        var parent = Directory.GetParent(assemblyDir)?.FullName;
        if (!string.IsNullOrWhiteSpace(parent))
        {
            foreach (var dataDir in Directory.GetDirectories(parent, "*_Data", SearchOption.TopDirectoryOnly))
            {
                AddIfExists(resolver, Path.Combine(dataDir, "Managed"));
            }
        }
    }

    return resolver;
}

static ReaderParameters BuildReaderParameters(string assemblyPath, string? managedDir = null)
{
    var symbolPath = Path.ChangeExtension(assemblyPath, ".pdb");
    return new ReaderParameters
    {
        AssemblyResolver = BuildResolver(assemblyPath, managedDir),
        ReadSymbols = File.Exists(symbolPath),
        InMemory = true,
        ReadingMode = ReadingMode.Deferred,
    };
}

static AssemblyDefinition ReadAssembly(string assemblyPath, string? managedDir = null)
{
    return AssemblyDefinition.ReadAssembly(assemblyPath, BuildReaderParameters(assemblyPath, managedDir));
}

static int ScanInput(string assemblyPath)
{
    using var assembly = ReadAssembly(assemblyPath);

    var candidates = new List<MethodCandidate>();

    foreach (var type in EnumerateTypes(assembly.MainModule.Types))
    {
        foreach (var method in type.Methods)
        {
            if (!method.HasBody)
            {
                continue;
            }

            var hits = InspectMethod(method);
            if (hits.Score == 0)
            {
                continue;
            }

            candidates.Add(new MethodCandidate(
                GetFriendlyMethodName(method),
                hits.Score,
                hits.Reasons,
                GetSourceLocation(method)));
        }
    }

    foreach (var candidate in candidates
        .OrderByDescending(c => c.Score)
        .ThenBy(c => c.MethodName, StringComparer.Ordinal)
        .Take(80))
    {
        Console.WriteLine($"{candidate.Score,3}  {candidate.MethodName}");
        if (!string.IsNullOrWhiteSpace(candidate.Location))
        {
            Console.WriteLine($"     {candidate.Location}");
        }

        Console.WriteLine($"     {string.Join("; ", candidate.Reasons)}");
    }

    return 0;
}

static int ScanStrings(string assemblyPath, string[] terms)
{
    if (terms.Length == 0)
    {
        Console.Error.WriteLine("scan-strings requires at least one search term.");
        return 1;
    }

    using var assembly = ReadAssembly(assemblyPath);

    foreach (var method in EnumerateTypes(assembly.MainModule.Types).SelectMany(t => t.Methods).Where(m => m.HasBody))
    {
        var matchedStrings = method.Body.Instructions
            .Where(i => i.OpCode == OpCodes.Ldstr)
            .Select(i => i.Operand as string)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.Ordinal)
            .Where(s => terms.Any(term => s!.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        if (matchedStrings.Length == 0)
        {
            continue;
        }

        Console.WriteLine(GetFriendlyMethodName(method));
        var location = GetSourceLocation(method);
        if (!string.IsNullOrWhiteSpace(location))
        {
            Console.WriteLine($"  {location}");
        }

        foreach (var matched in matchedStrings)
        {
            Console.WriteLine($"  \"{matched}\"");
        }
    }

    return 0;
}

static int ListTypes(string assemblyPath, string[] terms)
{
    if (terms.Length == 0)
    {
        Console.Error.WriteLine("list-types requires at least one search term.");
        return 1;
    }

    using var assembly = ReadAssembly(assemblyPath);
    foreach (var type in EnumerateTypes(assembly.MainModule.Types)
        .Where(t => terms.Any(term => t.FullName.Contains(term, StringComparison.OrdinalIgnoreCase)))
        .OrderBy(t => t.FullName, StringComparer.Ordinal))
    {
        Console.WriteLine(type.FullName);
    }

    return 0;
}

static int ListMethods(string assemblyPath, string[] terms)
{
    if (terms.Length == 0)
    {
        Console.Error.WriteLine("list-methods requires at least one search term.");
        return 1;
    }

    using var assembly = ReadAssembly(assemblyPath);
    foreach (var method in EnumerateTypes(assembly.MainModule.Types)
        .SelectMany(t => t.Methods)
        .Where(m => terms.Any(term =>
            m.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            m.DeclaringType.FullName.Contains(term, StringComparison.OrdinalIgnoreCase)))
        .OrderBy(m => m.DeclaringType.FullName, StringComparer.Ordinal)
        .ThenBy(m => m.Name, StringComparer.Ordinal))
    {
        Console.WriteLine(GetFriendlyMethodName(method));
    }

    return 0;
}

static int DumpMethod(string assemblyPath, string[] terms)
{
    if (terms.Length == 0)
    {
        Console.Error.WriteLine("dump-method requires at least one search term.");
        return 1;
    }

    using var assembly = ReadAssembly(assemblyPath);
    var methods = EnumerateTypes(assembly.MainModule.Types)
        .SelectMany(t => t.Methods)
        .Where(m => terms.All(term => GetFriendlyMethodName(m).Contains(term, StringComparison.OrdinalIgnoreCase)))
        .ToArray();

    if (methods.Length == 0)
    {
        Console.Error.WriteLine("No matching methods found.");
        return 1;
    }

    foreach (var method in methods)
    {
        Console.WriteLine(GetFriendlyMethodName(method));
        var location = GetSourceLocation(method);
        if (!string.IsNullOrWhiteSpace(location))
        {
            Console.WriteLine($"  {location}");
        }

        if (!method.HasBody)
        {
            Console.WriteLine("  <no body>");
            continue;
        }

        foreach (var instruction in method.Body.Instructions)
        {
            Console.WriteLine($"  {instruction.Offset:X4}: {instruction.OpCode,-12} {FormatOperand(instruction.Operand)}");
        }
    }

    return 0;
}

static int DumpType(string assemblyPath, string[] terms)
{
    if (terms.Length == 0)
    {
        Console.Error.WriteLine("dump-type requires at least one search term.");
        return 1;
    }

    using var assembly = ReadAssembly(assemblyPath);
    var types = EnumerateTypes(assembly.MainModule.Types)
        .Where(t => terms.All(term => t.FullName.Contains(term, StringComparison.OrdinalIgnoreCase)))
        .ToArray();

    if (types.Length == 0)
    {
        Console.Error.WriteLine("No matching types found.");
        return 1;
    }

    foreach (var type in types)
    {
        Console.WriteLine(type.FullName);
        if (type.IsEnum)
        {
            var underlying = type.Fields.FirstOrDefault(f => f.Name == "value__")?.FieldType.FullName;
            if (!string.IsNullOrWhiteSpace(underlying))
            {
                Console.WriteLine($"  enum-underlying: {underlying}");
            }
        }

        foreach (var field in type.Fields)
        {
            if (field.Name == "value__")
            {
                continue;
            }

            var constant = field.HasConstant ? $" = {field.Constant}" : string.Empty;
            Console.WriteLine($"  field {field.FieldType.FullName} {field.Name}{constant}");
        }

        foreach (var method in type.Methods.OrderBy(m => m.Name, StringComparer.Ordinal))
        {
            Console.WriteLine($"  method {method.Name}");
        }
    }

    return 0;
}

static int PatchAutorun(string assemblyPath, string[] remainingArgs)
{
    if (remainingArgs.Length == 0)
    {
        Console.Error.WriteLine("patch-autorun requires an output assembly path.");
        return 1;
    }

    var outputAssemblyPath = Path.GetFullPath(remainingArgs[0]);
    var managedDir = remainingArgs.Length >= 2 ? Path.GetFullPath(remainingArgs[1]) : null;

    Directory.CreateDirectory(Path.GetDirectoryName(outputAssemblyPath)!);

    var symbolPath = Path.ChangeExtension(assemblyPath, ".pdb");
    var outputSymbolPath = Path.ChangeExtension(outputAssemblyPath, ".pdb");

    using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath, BuildReaderParameters(assemblyPath, managedDir));
    var module = assembly.MainModule;

    var globalKeyHook = module.Types.FirstOrDefault(t => t.FullName == "TapTapLoot.TurnSystem.GlobalKeyHook");
    if (globalKeyHook == null)
    {
        Console.Error.WriteLine("Could not find TapTapLoot.TurnSystem.GlobalKeyHook.");
        return 1;
    }

    var startMethod = globalKeyHook.Methods.FirstOrDefault(m => m.Name == "Start" && m.HasBody);
    var autorunMethod = globalKeyHook.Methods.FirstOrDefault(m => m.Name == "AutorRun");
    if (startMethod == null || autorunMethod == null)
    {
        Console.Error.WriteLine("Could not find GlobalKeyHook.Start or GlobalKeyHook.AutorRun.");
        return 1;
    }

    if (!IsPatched(startMethod))
    {
        InjectAutorunPatch(module, startMethod, autorunMethod);
    }

    WriteAssemblyAndSymbols(assembly, symbolPath, outputAssemblyPath, outputSymbolPath);
    Console.WriteLine($"Patched assembly written to {outputAssemblyPath}");
    return 0;
}

static int PatchMythic(string assemblyPath, string[] remainingArgs)
{
    if (remainingArgs.Length == 0)
    {
        Console.Error.WriteLine("patch-mythic requires an output assembly path.");
        return 1;
    }

    var outputAssemblyPath = Path.GetFullPath(remainingArgs[0]);
    var managedDir = remainingArgs.Length >= 2 ? Path.GetFullPath(remainingArgs[1]) : null;

    Directory.CreateDirectory(Path.GetDirectoryName(outputAssemblyPath)!);

    var symbolPath = Path.ChangeExtension(assemblyPath, ".pdb");
    var outputSymbolPath = Path.ChangeExtension(outputAssemblyPath, ".pdb");

    using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath, BuildReaderParameters(assemblyPath, managedDir));
    PatchMythicInModule(assembly.MainModule);

    WriteAssemblyAndSymbols(assembly, symbolPath, outputAssemblyPath, outputSymbolPath);
    Console.WriteLine($"Patched assembly written to {outputAssemblyPath}");
    return 0;
}

static int PatchBoth(string assemblyPath, string[] remainingArgs)
{
    if (remainingArgs.Length == 0)
    {
        Console.Error.WriteLine("patch-both requires an output assembly path.");
        return 1;
    }

    var outputAssemblyPath = Path.GetFullPath(remainingArgs[0]);
    var managedDir = remainingArgs.Length >= 2 ? Path.GetFullPath(remainingArgs[1]) : null;

    Directory.CreateDirectory(Path.GetDirectoryName(outputAssemblyPath)!);

    var symbolPath = Path.ChangeExtension(assemblyPath, ".pdb");
    var outputSymbolPath = Path.ChangeExtension(outputAssemblyPath, ".pdb");

    using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath, BuildReaderParameters(assemblyPath, managedDir));
    var module = assembly.MainModule;

    var globalKeyHook = module.Types.FirstOrDefault(t => t.FullName == "TapTapLoot.TurnSystem.GlobalKeyHook");
    if (globalKeyHook == null)
    {
        Console.Error.WriteLine("Could not find TapTapLoot.TurnSystem.GlobalKeyHook.");
        return 1;
    }

    var startMethod = globalKeyHook.Methods.FirstOrDefault(m => m.Name == "Start" && m.HasBody);
    var autorunMethod = globalKeyHook.Methods.FirstOrDefault(m => m.Name == "AutorRun");
    if (startMethod == null || autorunMethod == null)
    {
        Console.Error.WriteLine("Could not find GlobalKeyHook.Start or GlobalKeyHook.AutorRun.");
        return 1;
    }

    if (!IsPatched(startMethod))
    {
        InjectAutorunPatch(module, startMethod, autorunMethod);
    }

    PatchMythicInModule(module);

    WriteAssemblyAndSymbols(assembly, symbolPath, outputAssemblyPath, outputSymbolPath);
    Console.WriteLine($"Patched assembly written to {outputAssemblyPath}");
    return 0;
}

static void PatchMythicInModule(ModuleDefinition module)
{
    var itemRegistryType = module.Types.FirstOrDefault(t => t.FullName == "TapTapLoot.InventorySystem.ItemRegistrySO");
    if (itemRegistryType == null)
    {
        throw new InvalidOperationException("Could not find TapTapLoot.InventorySystem.ItemRegistrySO.");
    }

    var getRandomRarityByLevel = itemRegistryType.Methods.FirstOrDefault(m =>
        m.Name == "GetRandomRarityByLevel" &&
        m.Parameters.Count == 2);

    if (getRandomRarityByLevel == null)
    {
        throw new InvalidOperationException("Could not find ItemRegistrySO.GetRandomRarityByLevel(int, Rarity).");
    }

    ForceReturnEnum(getRandomRarityByLevel, 6);
}

static void WriteAssemblyAndSymbols(
    AssemblyDefinition assembly,
    string symbolPath,
    string outputAssemblyPath,
    string outputSymbolPath)
{
    assembly.Write(outputAssemblyPath, new WriterParameters
    {
        WriteSymbols = File.Exists(symbolPath),
    });

    if (File.Exists(symbolPath) && !File.Exists(outputSymbolPath))
    {
        File.Copy(symbolPath, outputSymbolPath, overwrite: true);
    }
}

static void ForceReturnEnum(MethodDefinition method, int enumValue)
{
    method.Body.Variables.Clear();
    method.Body.ExceptionHandlers.Clear();
    method.Body.Instructions.Clear();
    method.Body.InitLocals = false;

    var il = method.Body.GetILProcessor();
    il.Append(il.Create(OpCodes.Ldc_I4, enumValue));
    il.Append(il.Create(OpCodes.Ret));
}

static int SimulateHighTierDrops(string[] args)
{
    if (args.Length == 0 || !int.TryParse(args[0], out var count) || count <= 0)
    {
        Console.Error.WriteLine("simulate-high-tier requires a positive count.");
        return 1;
    }

    var legendaryRatio = 0.5f;
    if (args.Length >= 2 && !float.TryParse(args[1], out legendaryRatio))
    {
        Console.Error.WriteLine("legendary ratio must be a number between 0 and 1.");
        return 1;
    }

    if (legendaryRatio is < 0f or > 1f)
    {
        Console.Error.WriteLine("legendary ratio must be between 0 and 1.");
        return 1;
    }

    var random = new Random(1337);
    var legendaryCount = 0;
    var mythicCount = 0;

    Console.WriteLine($"Simulating {count} forced high-tier drops...");
    Console.WriteLine($"Legendary ratio: {legendaryRatio:P0}");
    Console.WriteLine($"Mythic ratio: {(1f - legendaryRatio):P0}");
    Console.WriteLine();

    for (var i = 0; i < count; i++)
    {
        var rarity = random.NextDouble() < legendaryRatio ? "Legendary" : "Mythic";
        if (rarity == "Legendary")
        {
            legendaryCount++;
        }
        else
        {
            mythicCount++;
        }

        Console.WriteLine($"{i + 1,4}: {rarity}");
    }

    Console.WriteLine();
    Console.WriteLine("Summary");
    Console.WriteLine($"  Legendary: {legendaryCount}");
    Console.WriteLine($"  Mythic: {mythicCount}");
    return 0;
}

static InspectionHits InspectMethod(MethodDefinition method)
{
    var score = 0;
    var reasons = new HashSet<string>(StringComparer.Ordinal);
    var methodName = method.Name;
    var typeName = method.DeclaringType.Name;

    ScoreIfNameLooksRelevant(methodName, typeName, reasons, ref score);

    foreach (var instruction in method.Body.Instructions)
    {
        switch (instruction.Operand)
        {
            case MethodReference methodReference:
                ScoreMethodReference(methodReference, reasons, ref score);
                break;
            case FieldReference fieldReference:
                ScoreFieldReference(fieldReference, reasons, ref score);
                break;
            case string constantText:
                ScoreString(constantText, reasons, ref score);
                break;
        }
    }

    return new InspectionHits(score, reasons.OrderBy(x => x, StringComparer.Ordinal).ToArray());
}

static void ScoreIfNameLooksRelevant(string methodName, string typeName, ISet<string> reasons, ref int score)
{
    if (ContainsAny(methodName, "Move", "Movement", "Walk", "Input", "Player", "Controller"))
    {
        score += 2;
        reasons.Add($"method-name={methodName}");
    }

    if (ContainsAny(typeName, "Player", "Move", "Input", "Controller", "Character"))
    {
        score += 1;
        reasons.Add($"type-name={typeName}");
    }
}

static void ScoreMethodReference(MethodReference methodReference, ISet<string> reasons, ref int score)
{
    var fullName = methodReference.FullName;
    var declaringType = methodReference.DeclaringType.FullName;

    if (declaringType.Contains("UnityEngine.Input", StringComparison.Ordinal))
    {
        score += 8;
        reasons.Add($"calls={methodReference.Name}");
    }

    if (declaringType.Contains("UnityEngine.InputSystem", StringComparison.Ordinal))
    {
        score += 8;
        reasons.Add($"calls-inputsystem={methodReference.Name}");
    }

    if (methodReference.Name is "GetKey" or "GetKeyDown" or "GetKeyUp" or "GetAxis" or "GetAxisRaw")
    {
        score += 10;
        reasons.Add($"calls={methodReference.Name}");
    }

    if (methodReference.Name.Contains("ReadValue", StringComparison.Ordinal))
    {
        score += 6;
        reasons.Add($"calls={methodReference.Name}");
    }

    if (ContainsAny(fullName, "CharacterController", "Rigidbody", "MovePosition", "velocity", "Translate"))
    {
        score += 4;
        reasons.Add($"movement-call={methodReference.Name}");
    }
}

static void ScoreFieldReference(FieldReference fieldReference, ISet<string> reasons, ref int score)
{
    if (fieldReference.DeclaringType.FullName.Contains("UnityEngine.KeyCode", StringComparison.Ordinal))
    {
        score += 5;
        reasons.Add($"keycode={fieldReference.Name}");
    }
}

static void ScoreString(string constantText, ISet<string> reasons, ref int score)
{
    if (ContainsAny(constantText, "Horizontal", "Vertical", "Move", "Input", "Player"))
    {
        score += 4;
        reasons.Add($"string=\"{constantText}\"");
    }
}

static string GetFriendlyMethodName(MethodDefinition method)
{
    return $"{method.DeclaringType.FullName}::{method.Name}";
}

static string? GetSourceLocation(MethodDefinition method)
{
    if (!method.DebugInformation.HasSequencePoints)
    {
        return null;
    }

    var point = method.DebugInformation.SequencePoints.FirstOrDefault(p => !p.IsHidden);
    if (point == null)
    {
        return null;
    }

    return $"{Path.GetFileName(point.Document.Url)}:{point.StartLine}";
}

static IEnumerable<TypeDefinition> EnumerateTypes(IEnumerable<TypeDefinition> rootTypes)
{
    foreach (var type in rootTypes)
    {
        yield return type;

        foreach (var nested in EnumerateTypes(type.NestedTypes))
        {
            yield return nested;
        }
    }
}

static bool ContainsAny(string value, params string[] needles)
{
    return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
}

static string FormatOperand(object? operand)
{
    return operand switch
    {
        null => string.Empty,
        MethodReference method => method.FullName,
        FieldReference field => field.FullName,
        TypeReference type => type.FullName,
        Instruction target => $"IL_{target.Offset:X4}",
        Instruction[] targets => string.Join(", ", targets.Select(t => $"IL_{t.Offset:X4}")),
        string text => $"\"{text}\"",
        _ => operand.ToString() ?? string.Empty,
    };
}

static bool IsPatched(MethodDefinition startMethod)
{
    return startMethod.Body.Instructions.Any(i =>
        i.Operand is MethodReference methodRef &&
        methodRef.DeclaringType.FullName == "TapTapLoot.TurnSystem.GlobalKeyHook" &&
        methodRef.Name == "AutorRun");
}

static void InjectAutorunPatch(ModuleDefinition module, MethodDefinition startMethod, MethodDefinition autorunMethod)
{
    var unityCoreReference = module.AssemblyReferences.First(r => r.Name == "UnityEngine.CoreModule");
    var unityCoreAssembly = module.AssemblyResolver.Resolve(unityCoreReference);
    var applicationType = unityCoreAssembly.MainModule.Types.First(t => t.FullName == "UnityEngine.Application");
    var runInBackgroundSetter = applicationType.Methods.First(m => m.Name == "set_runInBackground");

    var importedRunInBackgroundSetter = module.ImportReference(runInBackgroundSetter);
    var importedAutorunMethod = module.ImportReference(autorunMethod);

    var body = startMethod.Body;
    var processor = body.GetILProcessor();
    var ret = body.Instructions.Last(i => i.OpCode == OpCodes.Ret);

    processor.InsertBefore(ret, processor.Create(OpCodes.Ldc_I4_1));
    processor.InsertBefore(ret, processor.Create(OpCodes.Call, importedRunInBackgroundSetter));
    processor.InsertBefore(ret, processor.Create(OpCodes.Ldarg_0));
    processor.InsertBefore(ret, processor.Create(OpCodes.Call, importedAutorunMethod));
}

internal sealed record MethodCandidate(string MethodName, int Score, string[] Reasons, string? Location);
internal sealed record InspectionHits(int Score, string[] Reasons);
