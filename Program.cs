using Microsoft.Win32;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Usage:
//   Interactive menu: dotnet run
//   With flags:       dotnet run -- "D:\...\Scam With Your Friends" [options]
//
// Options:
//   --host-tts / --vanilla-tts         1. Offload all TTS to Lobby Host (default) vs Vanilla per-client TTS
//   --prebuffer / --instant-speech     2. Wait for full sentence generation (default) vs Instant streaming speech
//   --gpu / --hybrid / --cpu           3. Hardware mode: GPU (default) vs Hybrid GPU+CPU vs CPU-only

const string GameFolderName = "Scam With Your Friends";

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.InputEncoding = System.Text.Encoding.UTF8;

string game = args.FirstOrDefault(a => !a.StartsWith("--") && Directory.Exists(a)) ?? FindGame();
if (game == null || !Directory.Exists(game))
{
    Console.WriteLine("Папка с игрой не найдена. Передайте путь к папке игры аргументом.");
    return 1;
}

string managed = Path.Combine(game, GameFolderName + "_Data", "Managed");
string asmCs = Path.Combine(managed, "Assembly-CSharp.dll");
string scripts = Path.Combine(managed, "ScriptsAssDef.dll");
Console.WriteLine("Игра: " + game);

if (System.Diagnostics.Process.GetProcessesByName(GameFolderName).Length > 0)
    Console.WriteLine("ВНИМАНИЕ: игра сейчас запущена! Закройте её перед применением патча.");

var testArg = args.FirstOrDefault(a => a.StartsWith("--test-worker"));
if (testArg != null)
{
    string backendName = testArg.Contains('=') ? testArg.Split('=')[1] : "Vulkan0";
    return TestWorker(game, backendName);
}

if (args.Contains("--dump"))
{
    var sb = new System.Text.StringBuilder();
    DumpFile(sb, managed, "Assembly-CSharp.dll", t => t.FullName.StartsWith("Kolkata.QwenTts."));
    DumpFile(sb, managed, "ScriptsAssDef.dll", t => t.Name.Contains("WhisperVc"));
    string outPath = Path.GetFullPath("swyf_dump.txt");
    File.WriteAllText(outPath, sb.ToString());
    Console.WriteLine("Dump written to: " + outPath);
    return 0;
}

// Default settings
ComputeMode computeMode = ComputeMode.HostOffload;
SpeechTimingMode timingMode = SpeechTimingMode.FullPrebuffer;
HardwareMode hardwareMode = HardwareMode.Gpu;

bool hasExplicitFlags = args.Any(a => a.StartsWith("--"));
if (hasExplicitFlags)
{
    if (args.Contains("--vanilla-tts")) computeMode = ComputeMode.Vanilla;
    else if (args.Contains("--host-tts")) computeMode = ComputeMode.HostOffload;

    if (args.Contains("--instant-speech")) timingMode = SpeechTimingMode.InstantStreaming;
    else if (args.Contains("--prebuffer")) timingMode = SpeechTimingMode.FullPrebuffer;

    if (args.Contains("--cpu") || args.Contains("--force-cpu")) hardwareMode = HardwareMode.CpuOnly;
    else if (args.Contains("--hybrid") || args.Contains("--gpu-cpu")) hardwareMode = HardwareMode.HybridGpuCpu;
    else if (args.Contains("--gpu")) hardwareMode = HardwareMode.Gpu;
}
else if (!Console.IsInputRedirected)
{
    RunInteractiveMenu(ref computeMode, ref timingMode, ref hardwareMode);
}

Console.WriteLine("\n========================================================");
Console.WriteLine(" Выбранная конфигурация патча:");
Console.WriteLine("  1. Вычисления в лобби : " + (computeMode == ComputeMode.HostOffload
    ? "ВСЕ НА ХОСТА (клиенты не нагружают ПК)"
    : "Обычные (каждый игрок считает сам)"));
Console.WriteLine("  2. Время ответа NPC   : " + (timingMode == SpeechTimingMode.FullPrebuffer
    ? "Дожидаться полной генерации фразы (без заиканий)"
    : "Обычное (говорить сразу по ходу генерации)"));
Console.WriteLine("  3. Режим оборудования : " + hardwareMode switch
{
    HardwareMode.Gpu => "Обычная (на видеокарту / GPU)",
    HardwareMode.HybridGpuCpu => "Распределение (Видеокарта + Процессор: TTS на GPU, Микрофон/STT на CPU)",
    HardwareMode.CpuOnly => "Только процессор (CPU-режим для малого объёма видеопамяти)",
    _ => hardwareMode.ToString()
});
Console.WriteLine("========================================================");

bool ok = true;
ok &= PatchAssembly(asmCs, managed, m => PatchAssemblyCSharp(m, computeMode, timingMode, hardwareMode));
ok &= PatchAssembly(scripts, managed, m => PatchScripts(m, hardwareMode));

SetRegistry();
CheckModels();

Console.WriteLine(ok ? "\nГотово! Патч успешно применён." : "\nЗавершено С ПРЕДУПРЕЖДЕНИЯМИ — см. сообщения выше.");
return ok ? 0 : 2;

// ---------------------------------------------------------------- Interactive Menu

static void RunInteractiveMenu(ref ComputeMode compute, ref SpeechTimingMode timing, ref HardwareMode hw)
{
    while (true)
    {
        Console.WriteLine();
        Console.WriteLine("======================================================================");
        Console.WriteLine("          НАСТРОЙКА МОДА-ПАТЧЕРА SCAM WITH YOUR FRIENDS");
        Console.WriteLine("======================================================================");
        Console.WriteLine($" [1] Вычисления в мультиплеере : {(compute == ComputeMode.HostOffload ? "ВСЕ НА ХОСТА (рекомендуется для слабых ПК друзей)" : "Обычные (каждый ПК считает сам)")}");
        Console.WriteLine($" [2] Время ответа персонажей   : {(timing == SpeechTimingMode.FullPrebuffer ? "Дожидаться полной генерации фразы (чёткая речь)" : "Обычное (сразу по ходу генерации)")}");
        string hwText = hw switch
        {
            HardwareMode.Gpu => "Обычная (на видеокарту / GPU)",
            HardwareMode.HybridGpuCpu => "Видеокарта + Процессор (TTS на GPU, распознавание речи на CPU)",
            HardwareMode.CpuOnly => "Только Процессор (CPU — экономия видеопамяти)",
            _ => ""
        };
        Console.WriteLine($" [3] Распределение нагрузки    : {hwText}");
        Console.WriteLine("----------------------------------------------------------------------");
        Console.WriteLine(" Введите номер (1, 2 или 3), чтобы переключить настройку,");
        Console.WriteLine(" или нажмите [Enter] / введите 0, чтобы ПРИМЕНИТЬ патч:");
        Console.Write(" > ");

        string input = Console.ReadLine()?.Trim() ?? "";
        if (string.IsNullOrEmpty(input) || input == "0")
            break;

        if (input == "1")
        {
            compute = compute == ComputeMode.HostOffload ? ComputeMode.Vanilla : ComputeMode.HostOffload;
        }
        else if (input == "2")
        {
            timing = timing == SpeechTimingMode.FullPrebuffer ? SpeechTimingMode.InstantStreaming : SpeechTimingMode.FullPrebuffer;
        }
        else if (input == "3")
        {
            hw = hw switch
            {
                HardwareMode.Gpu => HardwareMode.HybridGpuCpu,
                HardwareMode.HybridGpuCpu => HardwareMode.CpuOnly,
                _ => HardwareMode.Gpu
            };
        }
    }
}

// ---------------------------------------------------------------- infrastructure

static void DumpFile(System.Text.StringBuilder sb, string managedDir, string file, Func<TypeDefinition, bool> filter)
{
    string p = Path.Combine(managedDir, file);
    if (File.Exists(p + ".bak")) p += ".bak"; // always read the original
    if (!File.Exists(p)) { sb.AppendLine("##### missing: " + p); return; }

    using var resolver = new DefaultAssemblyResolver();
    resolver.AddSearchDirectory(managedDir);
    using var ms = new MemoryStream(File.ReadAllBytes(p));
    using var mod = ModuleDefinition.ReadModule(ms, new ReaderParameters { AssemblyResolver = resolver });
    sb.AppendLine("##### " + p);

    foreach (var t in mod.GetTypes().Where(filter))
    {
        sb.AppendLine();
        sb.AppendLine("=== TYPE " + t.FullName);
        foreach (var f in t.Fields) sb.AppendLine("  field " + f.FieldType.Name + " " + f.Name);
        foreach (var m in t.Methods.Where(x => x.HasBody))
        {
            sb.AppendLine("  -- " + m.FullName);
            foreach (var i in m.Body.Instructions)
            {
                string n = i.OpCode.Name;
                if (n.StartsWith("ldc") || n.StartsWith("call") || n == "newobj" || n.Contains("fld"))
                    sb.AppendLine($"     {i.Offset:X4} {n} {i.Operand}");
            }
        }
    }
}

// Launches QwenTtsWorker.exe the same way the game does and sends the Load command
// (int 1, talker path, codec path, backend). Prints timing, stderr, the worker's reply
// and peak memory (the game limits the worker to 4 GB of committed memory).
static int TestWorker(string game, string backend)
{
    string root = Path.Combine(game, "Scam With Your Friends_Data", "StreamingAssets", "kolkata-qwen-runtime");
    string exe = Path.Combine(root, "QwenTtsWorker.exe");
    string models = GetModelsDir();
    string talker = Path.Combine(models, "qwen-talker-0.6b-base-Q8_0.gguf");
    string codec = Path.Combine(models, "qwen-tokenizer-12hz-Q8_0.gguf");

    Console.WriteLine("=== Worker test, backend = " + backend);
    foreach (var f in new[] { exe, talker, codec })
        Console.WriteLine((File.Exists(f) ? "[found]   " : "[MISSING] ") + f);
    if (!File.Exists(exe) || !File.Exists(talker) || !File.Exists(codec)) return 1;

    var psi = new System.Diagnostics.ProcessStartInfo(exe)
    {
        WorkingDirectory = root,
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };

    using var p = System.Diagnostics.Process.Start(psi);
    p.ErrorDataReceived += (s, e) => { if (e.Data != null) Console.WriteLine("[worker stderr] " + e.Data); };
    p.BeginErrorReadLine();

    var sw = System.Diagnostics.Stopwatch.StartNew();
    var w = new BinaryWriter(p.StandardInput.BaseStream);
    var r = new BinaryReader(p.StandardOutput.BaseStream);
    w.Write(1);
    w.Write(talker);
    w.Write(codec);
    w.Write(backend);
    w.Flush();

    var reply = Task.Run(() => r.ReadInt32());
    long peakCommit = 0, peakWs = 0;
    while (!reply.IsCompleted && !p.HasExited && sw.Elapsed.TotalSeconds < 240)
    {
        try
        {
            p.Refresh();
            peakCommit = Math.Max(peakCommit, p.PeakPagedMemorySize64);
            peakWs = Math.Max(peakWs, p.PeakWorkingSet64);
        }
        catch { }
        Thread.Sleep(200);
    }

    Console.WriteLine($"Elapsed: {sw.Elapsed.TotalSeconds:F1} s");
    if (reply.IsCompleted && !reply.IsFaulted)
    {
        int code = reply.Result;
        Console.WriteLine("Worker replied with code: " + code);
        if (code == 4)
        {
            try { Console.WriteLine("Worker error message: " + r.ReadString()); } catch (Exception ex) { Console.WriteLine("Could not read message: " + ex.Message); }
        }
    }
    else if (p.HasExited)
        Console.WriteLine("WORKER EXITED before replying. Exit code: " + p.ExitCode);
    else
        Console.WriteLine("NO REPLY within 240 s (worker still running).");

    Console.WriteLine($"Peak committed memory: {peakCommit / 1048576} MB (game limit: 4096 MB)");
    Console.WriteLine($"Peak working set:      {peakWs / 1048576} MB");

    try { if (!p.HasExited) p.Kill(); } catch { }
    return 0;
}

static string FindGame()
{
    foreach (var d in DriveInfo.GetDrives())
    {
        string r = d.RootDirectory.FullName;
        foreach (var rel in new[]
        {
            @"SteamLibrary\steamapps\common",
            @"Program Files (x86)\Steam\steamapps\common",
            @"Steam\steamapps\common",
        })
        {
            string p = Path.Combine(r, rel, "Scam With Your Friends");
            if (Directory.Exists(p)) return p;
        }
    }
    return null;
}

static bool PatchAssembly(string path, string managedDir, Func<ModuleDefinition, bool> patch)
{
    Console.WriteLine("\n== " + Path.GetFileName(path));
    if (!File.Exists(path)) { Console.WriteLine("File not found."); return false; }

    string bak = path + ".bak";
    if (!File.Exists(bak)) File.Copy(path, bak);
    else File.Copy(bak, path, true); // always patch from the original backup!

    string tmp = path + ".patched";
    bool ok;
    using (var resolver = new DefaultAssemblyResolver())
    {
        resolver.AddSearchDirectory(managedDir);
        var readerParams = new ReaderParameters { AssemblyResolver = resolver };
        using var ms = new MemoryStream(File.ReadAllBytes(path));
        using (var module = ModuleDefinition.ReadModule(ms, readerParams))
        {
            ok = patch(module);
            module.Write(tmp);
        }
    }
    File.Copy(tmp, path, true);
    File.Delete(tmp);
    return ok;
}

static TypeDefinition FindType(ModuleDefinition m, string fullName) =>
    m.GetTypes().FirstOrDefault(t => t.FullName == fullName || t.Name == fullName);

static int ReplaceNum(MethodDefinition m, double from, double to)
{
    int n = 0;
    foreach (var i in m.Body.Instructions)
    {
        if (i.OpCode == OpCodes.Ldc_R4 && Math.Abs((float)i.Operand - (float)from) < 1e-6f)
        { i.Operand = (float)to; n++; }
        else if (i.OpCode == OpCodes.Ldc_R8 && Math.Abs((double)i.Operand - from) < 1e-9)
        { i.Operand = to; n++; }
    }
    return n;
}

static int CountNum(MethodDefinition m, double value)
{
    int n = 0;
    foreach (var i in m.Body.Instructions)
    {
        if (i.OpCode == OpCodes.Ldc_R4 && Math.Abs((float)i.Operand - (float)value) < 1e-6f) n++;
        else if (i.OpCode == OpCodes.Ldc_R8 && Math.Abs((double)i.Operand - value) < 1e-9) n++;
    }
    return n;
}

static bool Check(string what, int count)
{
    bool good = count > 0;
    Console.WriteLine((good ? "[OK]   " : "[FAIL] ") + what + " (" + count + " change(s))");
    return good;
}

// ---------------------------------------------------------------- Assembly-CSharp.dll

static bool PatchAssemblyCSharp(ModuleDefinition mod, ComputeMode computeMode, SpeechTimingMode timingMode, HardwareMode hardwareMode)
{
    bool ok = true;

    // 1a. QwenWorkerClient.HardwareEligible -> always true (GPU or forced CPU)
    var worker = FindType(mod, "Kolkata.QwenTts.QwenWorkerClient");
    var he = worker?.Methods.FirstOrDefault(x => x.Name == "HardwareEligible");
    if (he == null) { Console.WriteLine("[FAIL] HardwareEligible not found"); ok = false; }
    else ok &= PatchHardwareEligible(he, forceCpu: hardwareMode == HardwareMode.CpuOnly);

    var single = FindType(mod, "Kolkata.QwenTts.QwenTtsSingleton");
    if (single == null) { Console.WriteLine("[FAIL] QwenTtsSingleton not found"); return false; }

    // 1b-1. <Prepare> state machine: 20.0 -> 120.0
    int prepN = 0;
    var prepMethods = single.NestedTypes
        .Where(t => t.Name.StartsWith("<Prepare>"))
        .SelectMany(t => t.Methods.Where(m => m.Name == "MoveNext" && m.HasBody))
        .ToList();
    foreach (var pm in prepMethods)
    {
        var ins = pm.Body.Instructions;
        for (int i = 0; i < ins.Count - 1; i++)
        {
            if ((ins[i].OpCode == OpCodes.Ldc_R8 || ins[i].OpCode == OpCodes.Ldc_R4) &&
                ins[i + 1].OpCode == OpCodes.Call &&
                ins[i + 1].Operand is MethodReference mr && mr.Name == "FromSeconds")
            {
                double val = ins[i].OpCode == OpCodes.Ldc_R8 ? (double)ins[i].Operand : (float)ins[i].Operand;
                if (Math.Abs(val - 120.0) < 1e-6)
                {
                    prepN++;
                }
                else
                {
                    ins[i].OpCode = OpCodes.Ldc_R8;
                    ins[i].Operand = 120.0;
                    prepN++;
                }
            }
        }
    }
    if (prepN == 0)
    {
        prepN = prepMethods.Sum(x => ReplaceNum(x, 20.0, 120.0) + CountNum(x, 120.0));
    }
    ok &= Check("Prepare timeout is 120 s", prepN);

    // 1b-2. Prompt cancel timeout & firstAudioDeadline -> 300 s
    var promptMethods = single.Methods.Where(x => x.Name == "Prompt" && x.HasBody)
        .Concat(single.NestedTypes.Where(t => t.Name.Contains("<Prompt>"))
            .SelectMany(t => t.Methods.Where(x => x.Name == "MoveNext" && x.HasBody))).ToList();

    int promptCancelN = 0;
    foreach (var pm in promptMethods)
    {
        var ins = pm.Body.Instructions;
        for (int i = 0; i < ins.Count - 2; i++)
        {
            if ((ins[i].OpCode == OpCodes.Ldc_R8 || ins[i].OpCode == OpCodes.Ldc_R4) &&
                ins[i + 1].OpCode == OpCodes.Call &&
                ins[i + 1].Operand is MethodReference mr && mr.Name == "FromSeconds" &&
                ins[i + 2].OpCode == OpCodes.Newobj &&
                ins[i + 2].Operand is MethodReference nmr && nmr.DeclaringType.Name.Contains("CancellationTokenSource"))
            {
                double val = ins[i].OpCode == OpCodes.Ldc_R8 ? (double)ins[i].Operand : (float)ins[i].Operand;
                if (Math.Abs(val - 300.0) < 1e-6)
                {
                    promptCancelN++;
                }
                else
                {
                    ins[i].OpCode = OpCodes.Ldc_R8;
                    ins[i].Operand = 300.0;
                    promptCancelN++;
                }
            }
        }
    }
    if (promptCancelN == 0)
    {
        promptCancelN = promptMethods.Sum(x =>
            ReplaceNum(x, 12.0, 300.0) + ReplaceNum(x, 30.0, 300.0) + ReplaceNum(x, 60.0, 300.0) + CountNum(x, 300.0));
    }
    ok &= Check("Prompt cancel timeout -> 300 s", promptCancelN);

    int deadlineN = 0;
    foreach (var pm in promptMethods)
    {
        var ins = pm.Body.Instructions;
        for (int i = 0; i < ins.Count; i++)
        {
            if (ins[i].OpCode == OpCodes.Stfld && ins[i].Operand is FieldReference fr && fr.Name == "firstAudioDeadline")
            {
                for (int j = Math.Max(0, i - 3); j < i; j++)
                {
                    if (ins[j].OpCode == OpCodes.Ldc_R8 || ins[j].OpCode == OpCodes.Ldc_R4)
                    {
                        double val = ins[j].OpCode == OpCodes.Ldc_R8 ? (double)ins[j].Operand : (float)ins[j].Operand;
                        if (Math.Abs(val - 300.0) < 1e-6)
                        {
                            deadlineN++;
                        }
                        else
                        {
                            ins[j].OpCode = OpCodes.Ldc_R8;
                            ins[j].Operand = 300.0;
                            deadlineN++;
                        }
                        break;
                    }
                }
            }
        }
    }
    if (deadlineN == 0)
    {
        deadlineN = promptMethods.Sum(x =>
            ReplaceNum(x, 7.0, 300.0) + ReplaceNum(x, 20.0, 300.0) + ReplaceNum(x, 45.0, 300.0) + CountNum(x, 300.0));
    }
    ok &= Check("Prompt firstAudioDeadline -> 300 s", deadlineN);

    // per-chunk cancel timer inside the synthesis callback: 30 s -> 120 s
    int chunkN = 0;
    foreach (var nt in single.NestedTypes)
    {
        foreach (var m in nt.Methods.Where(x => x.HasBody))
        {
            var ins = m.Body.Instructions;
            for (int i = 0; i < ins.Count - 2; i++)
            {
                if ((ins[i].OpCode == OpCodes.Ldc_R8 || ins[i].OpCode == OpCodes.Ldc_R4) &&
                    ins[i + 1].OpCode == OpCodes.Call &&
                    ins[i + 1].Operand is MethodReference mr && mr.Name == "FromSeconds" &&
                    (ins[i + 2].OpCode == OpCodes.Call || ins[i + 2].OpCode == OpCodes.Callvirt) &&
                    ins[i + 2].Operand is MethodReference cmr && cmr.Name == "CancelAfter")
                {
                    double val = ins[i].OpCode == OpCodes.Ldc_R8 ? (double)ins[i].Operand : (float)ins[i].Operand;
                    if (Math.Abs(val - 120.0) < 1e-6)
                    {
                        chunkN++;
                    }
                    else
                    {
                        ins[i].OpCode = OpCodes.Ldc_R8;
                        ins[i].Operand = 120.0;
                        chunkN++;
                    }
                }
            }
        }
    }
    if (chunkN == 0)
    {
        var chunkType = FindType(mod, "Kolkata.QwenTts.QwenTtsSingleton/<>c__DisplayClass39_1")
                     ?? FindType(mod, "Kolkata.QwenTts.QwenTtsSingleton/<>c__DisplayClass40_1");
        chunkN = chunkType == null ? 0 : chunkType.Methods.Where(x => x.HasBody).Sum(x => ReplaceNum(x, 30.0, 120.0) + CountNum(x, 120.0));
    }
    ok &= Check("Per-chunk cancel timer 30 -> 120 s", chunkN);

    // 1b-3. Update: NOP out "ldc.i4.2; call SpeechProviderSelection.set_Qwen"
    int nops = 0;
    foreach (var u in single.Methods.Where(x => x.Name == "Update" && x.HasBody))
    {
        var ins = u.Body.Instructions;
        for (int k = 1; k < ins.Count; k++)
        {
            if (ins[k].OpCode.Code != Code.Call || ins[k].Operand is not MethodReference mr) continue;
            if (mr.Name != "set_Qwen" || ins[k - 1].OpCode != OpCodes.Ldc_I4_2) continue;
            if (mr.HasThis) { Console.WriteLine("[WARN] set_Qwen is an instance call, skipped"); continue; }
            ins[k - 1].OpCode = OpCodes.Nop; ins[k - 1].Operand = null;
            ins[k].OpCode = OpCodes.Nop; ins[k].Operand = null;
            nops++;
        }
    }
    bool leftover = single.Methods.Where(x => x.Name == "Update" && x.HasBody)
        .Any(u => u.Body.Instructions.Any(i => i.Operand is MethodReference mr2 && mr2.Name == "set_Qwen"));
    ok &= Check("Update: no set_Qwen(Never) call left", nops + (leftover ? 0 : 1));

    // 1c. LoadWavChannels(..., 10) -> 3
    int wav = 0;
    var allSingleMethods = single.Methods.Concat(single.NestedTypes.SelectMany(t => t.Methods)).Where(m => m.HasBody);
    foreach (var m in allSingleMethods)
    {
        var ins = m.Body.Instructions;
        for (int k = 1; k < ins.Count; k++)
        {
            if (ins[k].Operand is not MethodReference mr || mr.Name != "LoadWavChannels") continue;
            var prev = ins[k - 1];
            if (prev.OpCode == OpCodes.Ldc_I4_3 ||
                (prev.OpCode == OpCodes.Ldc_I4 && (int)prev.Operand == 3) ||
                (prev.OpCode == OpCodes.Ldc_R4 && Math.Abs((float)prev.Operand - 3f) < 1e-6f))
            {
                wav++;
            }
            else if ((prev.OpCode == OpCodes.Ldc_I4_S && Convert.ToInt32(prev.Operand) == 10) ||
                     (prev.OpCode == OpCodes.Ldc_I4 && (int)prev.Operand == 10))
            {
                prev.OpCode = OpCodes.Ldc_I4_3;
                prev.Operand = null;
                wav++;
            }
            else if (prev.OpCode == OpCodes.Ldc_R4 && Math.Abs((float)prev.Operand - 10f) < 1e-6f)
            {
                prev.Operand = 3f;
                wav++;
            }
        }
    }
    ok &= Check("LoadWavChannels length is 3 s", wav);

    // 1c-2. BlockingCollection capacity 32 -> 65536 (prevents buffer lockups)
    ok &= PatchBlockingCollectionCapacity(single);

    // 1d. Speech Timing: Pre-buffering vs Instant Streaming
    if (timingMode == SpeechTimingMode.FullPrebuffer)
    {
        ok &= PatchUpdatePrebuffering(single);
    }
    else
    {
        Console.WriteLine("[OK]   Speech Timing: Instant streaming (vanilla behavior, speaks as chunks generate)");
    }

    // 1e. HasSpeechStalled watchdog -> 300 s
    var pocket = FindType(mod, "PocketTtsSingleton");
    var stall = pocket?.Methods.FirstOrDefault(x => x.Name == "HasSpeechStalled");
    if (stall != null)
    {
        int n = ReplaceNum(stall, 30.0, 300.0) + ReplaceNum(stall, 120.0, 300.0) + CountNum(stall, 300.0);
        ok &= Check("HasSpeechStalled watchdog -> 300 s", n);
    }

    // 1f. Compute Mode: Host-Side TTS Offload vs Vanilla per-client TTS
    if (computeMode == ComputeMode.HostOffload)
    {
        ok &= PatchHostSideTtsOffload(mod);
    }
    else
    {
        Console.WriteLine("[OK]   Multiplayer Compute: Vanilla (each client computes its own TTS)");
    }

    return ok;
}

static bool PatchHostSideTtsOffload(ModuleDefinition mod)
{
    Console.WriteLine("\n-- Multiplayer Host-Side TTS Offload");
    bool ok = true;

    var playerVoice = FindType(mod, "KolkataPlayerVoice");
    var pocketTts = FindType(mod, "PocketTtsSingleton");
    var workspace = FindType(mod, "ScammerWorkspace");
    var networkedCall = FindType(mod, "NetworkedCall");

    if (playerVoice == null || pocketTts == null || workspace == null || networkedCall == null)
    {
        Console.WriteLine($"[FAIL] Required types for Host-Side TTS Offload not found (pv={playerVoice != null}, pt={pocketTts != null}, ws={workspace != null}, nc={networkedCall != null})");
        return false;
    }

    var speakMethod = pocketTts.Methods.FirstOrDefault(m => m.Name == "Speak" && m.Parameters.Count == 5);
    var speakTrackedMethod = pocketTts.Methods.FirstOrDefault(m => m.Name == "SpeakTracked" && m.Parameters.Count == 5);
    var warmUpVoiceMethod = pocketTts.Methods.FirstOrDefault(m => m.Name == "WarmUpVoice" && m.Parameters.Count == 1);

    // Find BackendAiMessages::LocalizeForPhone reference from UserCode_TargetTts
    var localizeMethod = playerVoice.Methods
        .Where(m => m.HasBody)
        .SelectMany(m => m.Body.Instructions)
        .Select(i => i.Operand as MethodReference)
        .FirstOrDefault(mr => mr != null && mr.Name == "LocalizeForPhone");

    if (speakMethod == null || speakTrackedMethod == null || warmUpVoiceMethod == null || localizeMethod == null)
    {
        Console.WriteLine($"[FAIL] Required TTS methods not found (speak={speakMethod != null}, tracked={speakTrackedMethod != null}, warmUp={warmUpVoiceMethod != null}, localize={localizeMethod != null})");
        return false;
    }

    // 1. KolkataPlayerVoice::ServerSendTts -> execute TTS directly on Host instead of sending TargetTts to client
    var serverSendTts = playerVoice.Methods.FirstOrDefault(m => m.Name == "ServerSendTts" && m.HasBody);
    if (serverSendTts == null)
    {
        Console.WriteLine("[FAIL] KolkataPlayerVoice.ServerSendTts not found");
        ok = false;
    }
    else
    {
        var ins = serverSendTts.Body.Instructions;
        var stloc0 = ins.FirstOrDefault(i => i.OpCode == OpCodes.Stloc_0);
        if (stloc0 == null)
        {
            Console.WriteLine("[FAIL] stloc.0 not found in ServerSendTts");
            ok = false;
        }
        else
        {
            var il = serverSendTts.Body.GetILProcessor();
            while (stloc0.Next != null)
                il.Remove(stloc0.Next);

            var pText = serverSendTts.Parameters[1];
            var pVoice = serverSendTts.Parameters[2];
            var pToken = serverSendTts.Parameters[3];
            var pLang = serverSendTts.Parameters[4];
            var pFiller = serverSendTts.Parameters[5];

            var trackedLabel = il.Create(OpCodes.Ldarg_2);

            // text = BackendAiMessages.LocalizeForPhone(text, languageCode);
            il.Append(il.Create(OpCodes.Ldarg_2));
            il.Append(il.Create(OpCodes.Ldarg_S, pLang));
            il.Append(il.Create(OpCodes.Call, localizeMethod));
            il.Append(il.Create(OpCodes.Starg_S, pText));

            // if (completionToken != 0) goto trackedLabel;
            il.Append(il.Create(OpCodes.Ldarg_S, pToken));
            il.Append(il.Create(OpCodes.Brtrue_S, trackedLabel));

            // PocketTtsSingleton.Speak(text, voicePath, callNetId, languageCode, isFiller);
            il.Append(il.Create(OpCodes.Ldarg_2));
            il.Append(il.Create(OpCodes.Ldarg_3));
            il.Append(il.Create(OpCodes.Ldloc_0));
            il.Append(il.Create(OpCodes.Ldarg_S, pLang));
            il.Append(il.Create(OpCodes.Ldarg_S, pFiller));
            il.Append(il.Create(OpCodes.Call, speakMethod));
            il.Append(il.Create(OpCodes.Ret));

            // trackedLabel: PocketTtsSingleton.SpeakTracked(text, voicePath, callNetId, completionToken, languageCode);
            il.Append(trackedLabel);
            il.Append(il.Create(OpCodes.Ldarg_3));
            il.Append(il.Create(OpCodes.Ldloc_0));
            il.Append(il.Create(OpCodes.Ldarg_S, pToken));
            il.Append(il.Create(OpCodes.Ldarg_S, pLang));
            il.Append(il.Create(OpCodes.Call, speakTrackedMethod));
            il.Append(il.Create(OpCodes.Ret));

            Console.WriteLine("[OK]   KolkataPlayerVoice.ServerSendTts -> redirected to Host PocketTtsSingleton");
        }
    }

    // 2. KolkataPlayerVoice::UserCode_CmdTts__String__String__String -> execute on Host
    var cmdTts = playerVoice.Methods.FirstOrDefault(m => m.Name.StartsWith("UserCode_CmdTts") && m.HasBody);
    if (cmdTts != null)
    {
        var body = cmdTts.Body;
        body.Instructions.Clear();
        var il = body.GetILProcessor();
        // PocketTtsSingleton.Speak(BackendAiMessages.LocalizeForPhone(text, languageCode), voicePath, 0u, languageCode, false);
        il.Append(il.Create(OpCodes.Ldarg_1));
        il.Append(il.Create(OpCodes.Ldarg_3));
        il.Append(il.Create(OpCodes.Call, localizeMethod));
        il.Append(il.Create(OpCodes.Ldarg_2));
        il.Append(il.Create(OpCodes.Ldc_I4_0));
        il.Append(il.Create(OpCodes.Ldarg_3));
        il.Append(il.Create(OpCodes.Ldc_I4_0));
        il.Append(il.Create(OpCodes.Call, speakMethod));
        il.Append(il.Create(OpCodes.Ret));
        Console.WriteLine("[OK]   KolkataPlayerVoice.CmdTts -> redirected to Host PocketTtsSingleton");
    }

    // 3. KolkataPlayerVoice::ServerWarmUpTts -> warm up voice on Host instead of client
    var serverWarmUp = playerVoice.Methods.FirstOrDefault(m => m.Name == "ServerWarmUpTts" && m.HasBody);
    if (serverWarmUp == null)
    {
        Console.WriteLine("[FAIL] KolkataPlayerVoice.ServerWarmUpTts not found");
        ok = false;
    }
    else
    {
        var ins = serverWarmUp.Body.Instructions;
        int patchedWarmUp = 0;
        for (int i = 0; i < ins.Count; i++)
        {
            if (ins[i].OpCode == OpCodes.Call && ins[i].Operand is MethodReference mr && mr.Name == "TargetWarmUpTts")
            {
                if (i >= 2 && ins[i - 2].OpCode == OpCodes.Ldarg_0)
                {
                    ins[i - 2].OpCode = OpCodes.Nop;
                    ins[i].Operand = warmUpVoiceMethod;
                    patchedWarmUp++;
                }
            }
            else if (ins[i].OpCode == OpCodes.Call && ins[i].Operand is MethodReference mr2 && mr2.Name == "WarmUpVoice")
            {
                patchedWarmUp++;
            }
        }
        ok &= Check("KolkataPlayerVoice.ServerWarmUpTts -> warms up voice on Host", patchedWarmUp);
    }

    // 4. ScammerWorkspace::OnTtsCallFrameGenerated -> pass fromServer: base.isServer to PublishSpeakerFrame
    var onFrame = workspace.Methods.FirstOrDefault(m => m.Name == "OnTtsCallFrameGenerated" && m.HasBody);
    if (onFrame == null)
    {
        Console.WriteLine("[FAIL] ScammerWorkspace.OnTtsCallFrameGenerated not found");
        ok = false;
    }
    else
    {
        var getIsServer = mod.GetTypes()
            .SelectMany(t => t.Methods.Where(m => m.HasBody))
            .SelectMany(m => m.Body.Instructions)
            .Select(i => i.Operand as MethodReference)
            .FirstOrDefault(mr => mr != null && mr.Name == "get_isServer" && mr.DeclaringType.Name == "NetworkBehaviour");

        if (getIsServer == null)
        {
            Console.WriteLine("[FAIL] NetworkBehaviour.get_isServer reference not found");
            ok = false;
        }
        else
        {
            var ins = onFrame.Body.Instructions;
            var pubCall = ins.FirstOrDefault(i => i.OpCode == OpCodes.Call &&
                i.Operand is MethodReference mr && mr.Name == "PublishSpeakerFrame");
            if (pubCall == null)
            {
                Console.WriteLine("[FAIL] PublishSpeakerFrame call not found in OnTtsCallFrameGenerated");
                ok = false;
            }
            else if (pubCall.Previous?.OpCode == OpCodes.Ldc_I4_0)
            {
                var il = onFrame.Body.GetILProcessor();
                var prev = pubCall.Previous;
                prev.OpCode = OpCodes.Ldarg_0;
                il.InsertBefore(pubCall, il.Create(OpCodes.Call, getIsServer));
                Console.WriteLine("[OK]   ScammerWorkspace.OnTtsCallFrameGenerated -> streams via RpcServerSpeakerFrame when on Host");
            }
            else if (pubCall.Previous?.OpCode == OpCodes.Call &&
                     pubCall.Previous.Operand is MethodReference pmr && pmr.Name == "get_isServer")
            {
                Console.WriteLine("[OK]   ScammerWorkspace.OnTtsCallFrameGenerated already patched");
            }
            else
            {
                Console.WriteLine("[FAIL] Unexpected IL before PublishSpeakerFrame in OnTtsCallFrameGenerated");
                ok = false;
            }
        }
    }

    // 5. NetworkedCall::ServerCanAcceptTrackedTtsCompletion -> allow Host's KolkataPlayerVoice to report completion for any call
    var canAccept = networkedCall.Methods.FirstOrDefault(m => m.Name == "ServerCanAcceptTrackedTtsCompletion" && m.HasBody);
    if (canAccept == null)
    {
        Console.WriteLine("[FAIL] NetworkedCall.ServerCanAcceptTrackedTtsCompletion not found");
        ok = false;
    }
    else
    {
        var ins = canAccept.Body.Instructions;
        var ineq = ins.FirstOrDefault(i => i.OpCode == OpCodes.Call &&
            i.Operand is MethodReference mr && mr.Name == "op_Inequality");
        if (ineq == null)
        {
            Console.WriteLine("[FAIL] op_Inequality not found in ServerCanAcceptTrackedTtsCompletion");
            ok = false;
        }
        else
        {
            var il = canAccept.Body.GetILProcessor();
            while (ineq.Next != null)
                il.Remove(ineq.Next);
            il.Append(il.Create(OpCodes.Ret));
            Console.WriteLine("[OK]   NetworkedCall.ServerCanAcceptTrackedTtsCompletion -> accepts Host TTS completion for all calls");
        }
    }

    return ok;
}

static bool PatchBlockingCollectionCapacity(TypeDefinition single)
{
    var prompt = single.Methods.FirstOrDefault(x => x.Name == "Prompt" && x.HasBody);
    if (prompt == null)
    {
        Console.WriteLine("[FAIL] QwenTtsSingleton.Prompt not found");
        return false;
    }

    int n = 0;
    var ins = prompt.Body.Instructions;
    for (int k = 1; k < ins.Count; k++)
    {
        if (ins[k].OpCode == OpCodes.Newobj &&
            ins[k].Operand is MethodReference mr &&
            mr.DeclaringType.Name.Contains("BlockingCollection"))
        {
            var prev = ins[k - 1];
            if ((prev.OpCode == OpCodes.Ldc_I4_S && Convert.ToInt32(prev.Operand) == 32) ||
                (prev.OpCode == OpCodes.Ldc_I4 && (int)prev.Operand == 32))
            {
                prev.OpCode = OpCodes.Ldc_I4;
                prev.Operand = 65536;
                n++;
            }
            else if (prev.OpCode == OpCodes.Ldc_I4 && (int)prev.Operand == 65536)
            {
                n++; // already patched
            }
        }
    }

    return Check("BlockingCollection capacity 32 -> 65536 (deadlock fix)", n);
}

static bool PatchUpdatePrebuffering(TypeDefinition single)
{
    var update = single.Methods.FirstOrDefault(x => x.Name == "Update" && x.HasBody);
    if (update == null)
    {
        Console.WriteLine("[FAIL] QwenTtsSingleton.Update not found");
        return false;
    }

    var workerField = single.Fields.FirstOrDefault(f => f.Name == "worker");
    if (workerField == null)
    {
        Console.WriteLine("[FAIL] QwenTtsSingleton.worker field not found");
        return false;
    }

    var ins = update.Body.Instructions;
    var target = ins.FirstOrDefault(i => i.OpCode == OpCodes.Call &&
        i.Operand is MethodReference mr && mr.Name == "get_unscaledTimeAsDouble");
    if (target == null)
    {
        Console.WriteLine("[FAIL] get_unscaledTimeAsDouble not found in Update");
        return false;
    }

    if (target.Previous?.OpCode == OpCodes.Ret &&
        target.Previous?.Previous?.OpCode == OpCodes.Beq &&
        target.Previous?.Previous?.Previous?.OpCode == OpCodes.Ldc_I4_5)
    {
        Console.WriteLine("[OK]   Pre-buffering: full sentence generation before playback already active");
        return true;
    }

    var getStatusMethod = ins.FirstOrDefault(i => i.Operand is MethodReference mr &&
        mr.Name == "get_Status" && mr.DeclaringType.FullName == "System.Threading.Tasks.Task")?.Operand as MethodReference;
    if (getStatusMethod == null)
    {
        Console.WriteLine("[FAIL] Task::get_Status reference not found in Update");
        return false;
    }

    var il = update.Body.GetILProcessor();
    var firstIns = il.Create(OpCodes.Ldarg_0);

    foreach (var i in ins)
    {
        if (i.Operand == target)
        {
            i.Operand = firstIns;
        }
    }

    il.InsertBefore(target, firstIns);
    il.InsertBefore(target, il.Create(OpCodes.Ldfld, workerField));
    il.InsertBefore(target, il.Create(OpCodes.Callvirt, getStatusMethod));
    il.InsertBefore(target, il.Create(OpCodes.Ldc_I4_5));
    il.InsertBefore(target, il.Create(OpCodes.Beq, target));
    il.InsertBefore(target, il.Create(OpCodes.Ret));

    Console.WriteLine("[OK]   Pre-buffering: audio plays smoothly only after full sentence is generated");
    return true;
}

static bool PatchHardwareEligible(MethodDefinition m, bool forceCpu = false)
{
    Console.WriteLine("HardwareEligible signature: " + m.FullName);

    static TypeReference Elem(ParameterDefinition p) =>
        p.ParameterType is ByReferenceType br ? br.ElementType : p.ParameterType;

    var vram = m.Parameters.FirstOrDefault(p => p.Name.ToLowerInvariant().Contains("vram"));
    var backend = m.Parameters.FirstOrDefault(p => p.Name == "backend")
                  ?? m.Parameters.FirstOrDefault(p => p.ParameterType.IsByReference && Elem(p).FullName == "System.String");

    if (vram == null || backend == null || !backend.ParameterType.IsByReference ||
        m.ReturnType.FullName != "System.Boolean")
    {
        Console.WriteLine("[FAIL] Unexpected signature (need vram param, 'out string backend', bool return).");
        return false;
    }

    string vt = Elem(vram).FullName;
    if (vt != "System.Int32" && vt != "System.Int64")
    {
        Console.WriteLine("[FAIL] vram type " + vt + " not supported by the patch.");
        return false;
    }
    bool isLong = vt == "System.Int64";

    var body = m.Body;
    body.Instructions.Clear();
    body.ExceptionHandlers.Clear();
    body.Variables.Clear();
    var il = body.GetILProcessor();

    foreach (var p in m.Parameters.Where(p => p != backend && p.ParameterType.IsByReference))
    {
        il.Append(il.Create(OpCodes.Ldarg, p));
        il.Append(il.Create(OpCodes.Initobj, Elem(p)));
    }

    if (forceCpu)
    {
        il.Append(il.Create(OpCodes.Ldarg, backend));
        il.Append(il.Create(OpCodes.Ldstr, "CPU"));
        il.Append(il.Create(OpCodes.Stind_Ref));
        il.Append(il.Create(OpCodes.Ldc_I4_1));
        il.Append(il.Create(OpCodes.Ret));
        Console.WriteLine("[OK]   HardwareEligible: FORCED CPU backend (low VRAM mode)");
        return true;
    }

    var cpu = il.Create(OpCodes.Nop);
    var end = il.Create(OpCodes.Nop);

    il.Append(il.Create(OpCodes.Ldarg, vram));
    if (vram.ParameterType.IsByReference)
        il.Append(il.Create(isLong ? OpCodes.Ldind_I8 : OpCodes.Ldind_I4));
    il.Append(isLong ? il.Create(OpCodes.Ldc_I8, 1024L) : il.Create(OpCodes.Ldc_I4, 1024));
    il.Append(il.Create(OpCodes.Blt, cpu));

    il.Append(il.Create(OpCodes.Ldarg, backend));
    il.Append(il.Create(OpCodes.Ldstr, "Vulkan0"));
    il.Append(il.Create(OpCodes.Stind_Ref));
    il.Append(il.Create(OpCodes.Br, end));

    il.Append(cpu);
    il.Append(il.Create(OpCodes.Ldarg, backend));
    il.Append(il.Create(OpCodes.Ldstr, "CPU"));
    il.Append(il.Create(OpCodes.Stind_Ref));

    il.Append(end);
    il.Append(il.Create(OpCodes.Ldc_I4_1));
    il.Append(il.Create(OpCodes.Ret));

    Console.WriteLine("[OK]   HardwareEligible: GPU (Vulkan0 if vram>=1024 MB, else CPU)");
    return true;
}

// ---------------------------------------------------------------- ScriptsAssDef.dll

static bool PatchScripts(ModuleDefinition mod, HardwareMode hardwareMode)
{
    bool ok = true;
    var t = mod.GetTypes().FirstOrDefault(x => x.Name == "WhisperVcPipeline");
    if (t == null) { Console.WriteLine("[FAIL] WhisperVcPipeline not found"); return false; }

    foreach (var c in t.Methods.Where(x => x.IsConstructor && !x.IsStatic && x.HasBody))
        ReplaceNum(c, 1.25, 0.65);

    var field = t.Fields.FirstOrDefault(f => f.Name == "silenceToFinalizeSeconds");
    var awake = t.Methods.FirstOrDefault(x => x.Name == "Awake" && x.HasBody);
    if (field == null || awake == null)
    {
        Console.WriteLine("[FAIL] field silenceToFinalizeSeconds or Awake not found");
        return false;
    }

    if (!awake.Body.Instructions.Any(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference fr && fr.Name == field.Name))
    {
        var il = awake.Body.GetILProcessor();
        var first = awake.Body.Instructions[0];
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(first, il.Create(OpCodes.Ldc_R4, 0.65f));
        il.InsertBefore(first, il.Create(OpCodes.Stfld, field));
        Console.WriteLine("[OK]   Awake: silenceToFinalizeSeconds = 0.65 forced at start");
    }
    else
    {
        Console.WriteLine("[OK]   Awake already forces silenceToFinalizeSeconds");
    }

    // If HybridGpuCpu or CpuOnly is selected, offload speech recognition (Whisper / Moonshine) to CPU
    // so GPU VRAM and compute are 100% dedicated to Qwen3-TTS + game rendering (or completely freed in CpuOnly mode).
    if (hardwareMode == HardwareMode.HybridGpuCpu || hardwareMode == HardwareMode.CpuOnly)
    {
        var compat = mod.GetTypes().FirstOrDefault(x => x.Name == "WindowsSpeechCompatibility");
        var needsCpu = compat?.Methods.FirstOrDefault(m => m.Name == "NeedsCpuRecognition" && m.HasBody);
        if (needsCpu == null)
        {
            Console.WriteLine("[FAIL] WindowsSpeechCompatibility.NeedsCpuRecognition not found");
            ok = false;
        }
        else
        {
            needsCpu.Body.Instructions.Clear();
            needsCpu.Body.Variables.Clear();
            needsCpu.Body.ExceptionHandlers.Clear();
            var il = needsCpu.Body.GetILProcessor();
            il.Append(il.Create(OpCodes.Ldc_I4_1));
            il.Append(il.Create(OpCodes.Ret));
            Console.WriteLine("[OK]   WindowsSpeechCompatibility.NeedsCpuRecognition -> true (Speech Recognition offloaded to CPU)");
        }
    }
    else
    {
        Console.WriteLine("[OK]   Speech Recognition hardware: default (GPU)");
    }

    return ok;
}

// ---------------------------------------------------------------- registry + models

static void SetRegistry()
{
    Console.WriteLine("\n== Registry");
    using var k = Registry.CurrentUser.CreateSubKey(@"Software\Jatater Worldwide\Scam With Your Friends");
    k.SetValue("Kolkata.Qwen3Tts_h1324939569", 0, RegistryValueKind.DWord);
    k.SetValue("Kolkata.Dev.RussianSpeechProvider_h3030369888", 0, RegistryValueKind.DWord);
    Console.WriteLine("[OK]   Qwen3Tts = 0, RussianSpeechProvider = 0");
}

static string GetModelsDir()
{
    string baseDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        @"AppData\LocalLow\Jatater Worldwide\Scam With Your Friends\qwen3-tts");
    if (!Directory.Exists(baseDir)) return baseDir;
    foreach (var d in Directory.GetDirectories(baseDir))
    {
        if (File.Exists(Path.Combine(d, "qwen-talker-0.6b-base-Q8_0.gguf")))
            return d;
    }
    return Directory.GetDirectories(baseDir).FirstOrDefault()
        ?? Path.Combine(baseDir, "b7ee2e8c7459c3bea99da23e3d178125a7d1713c");
}

static void CheckModels()
{
    Console.WriteLine("\n== Models");
    string dir = GetModelsDir();
    foreach (var f in new[] { "qwen-talker-0.6b-base-Q8_0.gguf", "qwen-tokenizer-12hz-Q8_0.gguf" })
    {
        string p = Path.Combine(dir, f);
        Console.WriteLine(File.Exists(p)
            ? $"[OK]   {f} ({new FileInfo(p).Length / 1048576} MB)"
            : $"[MISSING] {f} - launch the game and wait for the Qwen3 download bar to reach 100%");
    }
}

enum ComputeMode { HostOffload, Vanilla }
enum SpeechTimingMode { FullPrebuffer, InstantStreaming }
enum HardwareMode { Gpu, HybridGpuCpu, CpuOnly }
