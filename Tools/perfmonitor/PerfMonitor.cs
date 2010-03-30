// Copyright (c) Microsoft Corporation.  All rights reserved
// This file is best viewed using outline mode (Ctrl-M Ctrl-O)
//
// This program uses code hyperlinks available as part of the HyperAddin Visual Studio plug-in.
// It is available from http://www.codeplex.com/hyperAddin 
// 
using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Text.RegularExpressions;
using Utilities;
using Microsoft.Win32;
using System.Diagnostics.Eventing;
using FastSerialization;
using System.Reflection;
using Diagnostics.Eventing;

// See code:SystemManagement.PerfMonitor to get started. 

namespace SystemManagement
{
    /// <summary>
    /// A utility for profilinng the system using Event Tracing for Windows (ETW)
    /// 
    /// See code:#Help for usage.  
    /// </summary>
    sealed class PerfMonitor
    {
        public static int Main(string[] args)
        {
            CommandLineArgs parsedArgs = null;
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                parsedArgs = new CommandLineArgs(args);
                if (parsedArgs.help)
                {
                    Console.WriteLine("PerfMontitor: version 0.9.5000\r\n");
                    Console.Write(CommandLineArgs.UsageHelpText());
                    return 0;
                }

                // If you add something also update code:#Help
                // #OperationParsing
                if (string.Compare(parsedArgs.operation, "start", StringComparison.OrdinalIgnoreCase) == 0)
                    Start(parsedArgs);
                else if (string.Compare(parsedArgs.operation, "stop", StringComparison.OrdinalIgnoreCase) == 0)
                    Stop(parsedArgs);
                else if (string.Compare(parsedArgs.operation, "print", StringComparison.OrdinalIgnoreCase) == 0)
                    Print(parsedArgs);
                else if (string.Compare(parsedArgs.operation, "rawprint", StringComparison.OrdinalIgnoreCase) == 0)
                    RawPrint(parsedArgs);
                else if (string.Compare(parsedArgs.operation, "stats", StringComparison.OrdinalIgnoreCase) == 0)
                    Stats(parsedArgs);
                else if (string.Compare(parsedArgs.operation, "procs", StringComparison.OrdinalIgnoreCase) == 0)
                    Procs(parsedArgs);
                else if (string.Compare(parsedArgs.operation, "printCSV", StringComparison.OrdinalIgnoreCase) == 0)
                    PrintCSV(parsedArgs);
                else if (string.Compare(parsedArgs.operation, "run", StringComparison.OrdinalIgnoreCase) == 0)
                    Run(parsedArgs);
                else if (string.Compare(parsedArgs.operation, "runPrint", StringComparison.OrdinalIgnoreCase) == 0)
                    RunPrint(parsedArgs);
                else if (string.Compare(parsedArgs.operation, "list", StringComparison.OrdinalIgnoreCase) == 0)
                    List(parsedArgs);
                else if (string.Compare(parsedArgs.operation, "providerHelp", StringComparison.OrdinalIgnoreCase) == 0)
                    CommandLineArgs.ProviderHelp(parsedArgs);
                else if (string.Compare(parsedArgs.operation, "convert", StringComparison.OrdinalIgnoreCase) == 0)
                    Convert(parsedArgs);  // TODO document.  
                else if (string.Compare(parsedArgs.operation, "usersGuide", StringComparison.OrdinalIgnoreCase) == 0)
                    UsersGuide();
                else if (string.Compare(parsedArgs.operation, "crawlableCLRStacks", StringComparison.OrdinalIgnoreCase) == 0)
                    SetCrawlableClrStacks(true);
                else if (string.Compare(parsedArgs.operation, "defaultCLRStacks", StringComparison.OrdinalIgnoreCase) == 0)
                    SetCrawlableClrStacks(false);
                else if (string.Compare(parsedArgs.operation, "enableKernelStacks64", StringComparison.OrdinalIgnoreCase) == 0)
                    SetKernelStacks64(true);
                else if (string.Compare(parsedArgs.operation, "disableKernelStacks64", StringComparison.OrdinalIgnoreCase) == 0)
                    SetKernelStacks64(false);
                else if (string.Compare(parsedArgs.operation, "listSources", StringComparison.OrdinalIgnoreCase) == 0)
                    ListSources(parsedArgs);
                else if (string.Compare(parsedArgs.operation, "realtime", StringComparison.OrdinalIgnoreCase) == 0)
                    RealTime_Experimental(parsedArgs);
                else if (string.Compare(parsedArgs.operation, "monitorImages", StringComparison.OrdinalIgnoreCase) == 0)
                    RealTimeLoads_Experimental();
                else if (string.Compare(parsedArgs.operation, "monitorProcs", StringComparison.OrdinalIgnoreCase) == 0)
                    RealTimeProcs_Experimental();
                else if (string.Compare(parsedArgs.operation, "merge", StringComparison.OrdinalIgnoreCase) == 0)
                    Merge(parsedArgs);
                else if (string.Compare(parsedArgs.operation, "jitTime", StringComparison.OrdinalIgnoreCase) == 0)
                    JitTime(parsedArgs);
                else if (string.Compare(parsedArgs.operation, "GCTime", StringComparison.OrdinalIgnoreCase) == 0)
                    GCTime(parsedArgs);
                else
                {
                    if (Test_Experimental(parsedArgs))
                        return 0;

                    Console.WriteLine("Unrecognized operation " + parsedArgs.operation + ".  Use /? for help.");
                    return 1;
                }
            }
            catch (ApplicationException e)
            {
                // Errors that think are really for user consumption I used ApplicationException for.  
                // Just print the message and fail.  
                Console.WriteLine("Error: " + e.Message + ". Use /? for help.");
                return 1;
            }
            catch (Exception e)
            {
                if (e is SerializationException)
                    Console.WriteLine("Error: interpreting the ETLX file format.  Typically a version mismatched or corrupt ETLX file");
                else
                    Console.WriteLine("An exceptional condition occurred.");
                if (parsedArgs == null || parsedArgs.debug)
                    Console.WriteLine("Exception: " + e);
                else
                {
                    Console.WriteLine("Exception: " + e.Message);
                    Console.WriteLine("Use /debug for additional debugging information.");
                }
                Debug.Assert(false, "Exception thrown during processing");
                return 1;
            }

            sw.Stop();
            Console.WriteLine("PerfMonitor processing time: " + (sw.ElapsedMilliseconds / 1000.0).ToString("f3") + " secs.");
            return 0;
        }

        private static void Start(CommandLineArgs parsedArgs)
        {
            UnpackSupportDlls();
            try
            {
                if (parsedArgs.filePath1 == null)
                    parsedArgs.filePath1 = "PerfMonitorOutput.etl";
                else
                    parsedArgs.filePath1 = Path.ChangeExtension(parsedArgs.filePath1, ".etl");
                string kernelFileName = Path.ChangeExtension(parsedArgs.filePath1, ".kernel.etl");
                Console.WriteLine("Starting tracing.  Output file: " + parsedArgs.filePath1);
                TraceEventSession userModeSession = new TraceEventSession(UserModeSessionName, parsedArgs.filePath1);

                // Turn on the kernel providers
                if (!parsedArgs.noKernel)
                {
                    bool alreadyExist = userModeSession.EnableKernelProvider(parsedArgs.kernelFlags, parsedArgs.stacks ? parsedArgs.kernelFlags : KernelTraceEventParser.Keywords.None);
                    if (alreadyExist)
                        Console.WriteLine("Session '" + KernelTraceEventParser.KernelSessionName + "' already exists, stopping and restarting.");
                    Console.WriteLine("Kernel provider started Flags:0x{0:x} Stacks:{1} Session {2}.",
                        parsedArgs.kernelFlags, parsedArgs.stacks, KernelTraceEventParser.KernelSessionName);
                }
                else if (File.Exists(kernelFileName))
                    File.Delete(kernelFileName);

                // Turn on the User mode providers. 
                if (parsedArgs.providers.Count > 0)
                {
                    // Are there user mode providers to turn on?
                    foreach (CommandLineArgs.ProviderArgs provider in parsedArgs.providers)
                    {
                        bool success = false;
                        try
                        {
                            if (provider.Guid == ClrTraceEventParser.ProviderGuid ||
                                provider.Guid == ClrStressTraceEventParser.ProviderGuid)
                            {
                                if (Environment.Version.Build == 50727 && Environment.Version.MinorRevision < 1378)
                                {
                                    Console.WriteLine("Warning: The CLR is old and does not support CLR Method and Module events.");
                                    Console.WriteLine("         Symbolic resolution of CLR methods will not be possible.");
                                    Console.WriteLine("         Update to the latest CLR service pack to avoid this warning.");
                                }
                                if (parsedArgs.stacks && Environment.OSVersion.Version.Major > 5)
                                {
                                    // PerfMonitor is currently a 32 bit app (because of dbgHelp.dll), so it runs in the wow.
                                    Debug.Assert(Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") == "x86");
                                    string arch = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432");

                                    if (Environment.Version.Build == 50727 && Environment.Version.MinorRevision < 3053)
                                    {
                                        // Warn people if they are not already doing JIT framed. 
                                        if (Registry.GetValue(@"HKEY_LOCAL_MACHINE\Software\Microsoft\.NETFramework", "JitFramed", null) == null)
                                        {
                                            Console.WriteLine();
                                            Console.WriteLine(@"---------------------------------------------------------------------------");
                                            if (arch != null)  // we are on a 64 bit system
                                            {
                                                Console.WriteLine(@"Warning: CLR is not configured to generate crawlable stack frames for 32");
                                                Console.WriteLine(@"bit code (the limitation does not effect 64 bit code).  By default managed");
                                                Console.WriteLine(@"code will run 64 bit, but may run 32 bit because it interoperates with 32");
                                                Console.WriteLine(@"bit dlls.  If you desire stack traces on 32 bit managed code you need to");
                                                Console.WriteLine(@"either upgrate the CLR or configure the runtime for crawalabl stacks.");
                                            }
                                            else
                                            {
                                                Console.WriteLine(@"Warning: CLR is not configured to generate crawlable stack frames.");
                                                Console.WriteLine();
                                            }
                                            Console.WriteLine();
                                            Console.WriteLine(@"Turning on crawable stacks may cost ~3% performance loss on compute bound");
                                            Console.WriteLine(@"scenarios, but make stack based profiling possible.");
                                            Console.WriteLine();
                                            Console.WriteLine(@"Use 'perfMonitor crawlableCLRStacks' to make stacks crawlable.");
                                            Console.WriteLine(@"Use 'perfMonitor defaultCLRStacks' to restore the default.");
                                            Console.WriteLine();
                                            Console.WriteLine(@"However the best solution is to install .NET V3.5 Service Pack 1 (or later).");
                                            Console.WriteLine();
                                        }
                                    }

                                    if (arch != null)   // we are on a 64 bit system. 
                                    {
                                        if (!IsKernelStacks64Enabled())
                                        {
                                            Console.WriteLine();
                                            Console.WriteLine(@"---------------------------------------------------------------------------");
                                            Console.WriteLine(@"Warning: This is a 64 bit system but has not been configured in a way that");
                                            Console.WriteLine(@"that allows stack walking in the kernel.");
                                            Console.WriteLine();
                                            Console.WriteLine(@"Use 'perfMonitor enableKernelStacks64' to make stacks crawlable.");
                                            Console.WriteLine(@"Use 'perfMonitor disableKernelStacks64' to restore the default.");
                                            Console.WriteLine();
                                            Console.WriteLine(@"Note that enabling kernel stacks will dedicate the pages used by drivers");
                                            Console.WriteLine(@"for only those drivers.  This will typically increase Kernel memory");
                                            Console.WriteLine(@"consumption by 10s of MB.  This should be the only side effect.");
                                            Console.WriteLine();
                                        }
                                    }
                                }

                                // On an explict offset, also set the registry key that enables CLR ETW events (Pre Vista) 
                                if (string.Compare(parsedArgs.operation, "start", StringComparison.OrdinalIgnoreCase) == 0)
                                {
                                    bool changed;
                                    if (!EnableSystemWideCLREtw(true, out changed))
                                        Console.WriteLine(@"Warning: CLR Events explicitly turned off, remove HLKM\Software\Microsoft\.NETFramework\ETWEnabled to fix");
                                    if (changed)
                                    {
                                        Console.WriteLine(@"Warning: CLR Events turned on, but currently running processes will not be affected.");
                                        Console.WriteLine(@"The following command will fix permenently, at the cost of ~10 ms of startup time.");
                                        Console.WriteLine(@"     reg add HKLM\Software\Microsoft\.NETFramework /v ETWEnabled /d 1 /t REG_DWORD /f");
                                    }
                                }
                            }

                            bool alreadyExist = userModeSession.EnableProvider(provider.Guid,
                                provider.Level, provider.MatchAnyKeywords, provider.MatchAllKeywords, provider.providerData);
                            if (alreadyExist)
                                Console.WriteLine("Session '" + userModeSession.SessionName + "' already exists, stopping and restarting.");

                            Console.WriteLine("Provider {0} started, Session {1}.", provider.Guid, userModeSession.SessionName);
                            Console.WriteLine("    AnyKey:0x{0:x} AllKey:0x{1:x} Level:{2}",
                                provider.MatchAnyKeywords, provider.MatchAllKeywords, provider.Level);
                            success = true;
                        }
                        finally
                        {
                            if (!success)
                                userModeSession.Stop();
                        }
                    }
                }
                else if (File.Exists(parsedArgs.filePath1))
                    File.Delete(parsedArgs.filePath1);
            }
            catch (System.UnauthorizedAccessException)
            {
                throw new ApplicationException("Not authorized to perform start (Must be elevated on VISTA).");
            }
            catch (System.ArgumentException)
            {
                throw new ApplicationException(
                    "Starting a trace failed with an argument exception.\r\n" +
                    "This can happen when you are starting an trace that is already started.\r\n");
            }
        }
        private static void Stop(CommandLineArgs parsedArgs)
        {
            Console.WriteLine("Stopping tracing for sessions '" +
                KernelTraceEventParser.KernelSessionName + "' and '" + UserModeSessionName + "'.");

            // Try to force the rundown of CLR method and loader events
            if (parsedArgs.rundownClr > 0)
            {
                try
                {
                    Console.WriteLine("Sending rundown action to CLR provider.");
                    TraceEventSession clrSession = new TraceEventSession(UserModeSessionName);
                    clrSession.EnableProvider(ClrRundownTraceEventParser.ProviderGuid, TraceEventLevel.Verbose, (ulong)(
                        ClrRundownTraceEventParser.Keywords.End |
                        ClrRundownTraceEventParser.Keywords.NGen |
                        ClrRundownTraceEventParser.Keywords.Jit |
                        ClrRundownTraceEventParser.Keywords.Loader));
                    Console.WriteLine("Waiting {0} seconds to complete...", parsedArgs.rundownClr);
                    System.Threading.Thread.Sleep(parsedArgs.rundownClr * 1000);
                }
                catch (Exception e)
                {
                    Console.WriteLine("Warning: failure during CLR Rundown " + e.Message);
                }
            }

            TraceEventSession.StopUserAndKernelSession(UserModeSessionName);
            bool changed;
            EnableSystemWideCLREtw(false, out changed);

            if (!parsedArgs.noEtlx)
            {
                if (parsedArgs.filePath1 == null)
                    parsedArgs.filePath1 = "PerfMonitorOutput.etl";

                if (File.Exists(parsedArgs.filePath1))
                    Convert(parsedArgs);
                else
                    Console.WriteLine("Warning: no data generated at " + parsedArgs.filePath1 + ".\n");
            }
        }
        private static void Convert(CommandLineArgs parsedArgs)
        {
            UnpackSupportDlls();

            if (parsedArgs.filePath1 == null)
                parsedArgs.filePath1 = "PerfMonitorOutput.etl";

            TraceLogOptions options = new TraceLogOptions();
            options.SourceLineNumbers = parsedArgs.lineNumbers;
            options.SymbolDebug = parsedArgs.symDebug;
            Dictionary<string, string> dllSet = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!parsedArgs.noSym)
            {
                dllSet["ntdll"] = null;
                dllSet["kernel32"] = null;
                dllSet["advapi32"] = null;
                dllSet["ntkrnlpa"] = null;
                dllSet["ntoskrnl"] = null;
                // dllSet["clr"] = null;
                // dllSet["mscoree"] = null;
                // dllSet["mscorwks"] = null;
            }

            if (parsedArgs.dlls != null)
            {
                options.AlwaysResolveSymbols = true;
                foreach (string dll in parsedArgs.dlls)
                    dllSet[dll] = null;
            }
            options.ShouldResolveSymbols = delegate(string moduleFilePath)
            {
                string moduleName = Path.GetFileNameWithoutExtension(moduleFilePath);
                return dllSet.ContainsKey(moduleName);
            };

            // use the ETL symbolic information only if we are on a differnt machine than the trace machine.
            // <Make the user indicate that>, maybe /etlSymbols
            // TODO Enable *** options.SymbolResolver = SymbolResolverEnum.ETLSymbolResolver;
            Console.WriteLine("Converting " + Path.GetFileName(parsedArgs.filePath1) + " to ETLX format...");

            string etlxFileName = TraceLog.CreateFromETL(parsedArgs.filePath1, null, options);
            Console.WriteLine("Converted data in " + etlxFileName);
        }
        private static void Stats(CommandLineArgs parsedArgs)
        {
            using (TraceEventDispatcher source = GetSource(ref parsedArgs.filePath1))
            {
                Console.WriteLine("Computing Stats for " + parsedArgs.filePath1);

                TextWriter output = Console.Out;
                if (parsedArgs.filePath2 == null)
                    parsedArgs.filePath2 = Path.ChangeExtension(parsedArgs.filePath1, "stats.xml");
                if (parsedArgs.filePath2 != "-")
                    output = System.IO.File.CreateText(parsedArgs.filePath2);

                Console.WriteLine("Trace duration : " + source.SessionDuration);

                // Maps task X opcode -> count 
                EventStats eventStats = new EventStats(source);
                output.Write(eventStats.ToString());
                if (output != Console.Out)
                {
                    output.Close();
                    Console.WriteLine("Output in " + parsedArgs.filePath2);
                }
            }
        }
        private static void ListSources(CommandLineArgs parsedArgs)
        {
            if (parsedArgs.filePath1 == null)
                throw new ApplicationException("ListSources needs a file to search for event sources.");

            bool anySources = false;
            foreach (Type eventSource in CommandLineArgs.GetEventSourcesInFile(parsedArgs.filePath1))
            {
                if (!anySources)
                    Console.WriteLine("Event sources in " + parsedArgs.filePath1 + ".");

                Console.WriteLine("{0,-30}: {1}", CommandLineArgs.GetName(eventSource), CommandLineArgs.GetGuid(eventSource));
                anySources = true;
            }
            if (!anySources)
                Console.WriteLine("No event sources in " + parsedArgs.filePath1 + ".");
        }
        /// <summary>
        /// This command generates XPerfInfo compatible comma separated value (CSV) files
        /// </summary>
        /// <param name="parsedArgs"></param>
        private static void PrintCSV(CommandLineArgs parsedArgs)
        {
            if (parsedArgs.filePath1 == null)
                parsedArgs.filePath1 = "PerfMonitorOutput.etlx";

            Console.WriteLine("Converting " + parsedArgs.filePath1 + " to a comma separated value (CSV) file");
            if (parsedArgs.filePath2 == null)
                parsedArgs.filePath2 = Path.ChangeExtension(parsedArgs.filePath1, ".csv");

            using (StreamWriter outFile = System.IO.File.CreateText(parsedArgs.filePath2))
            {
                TraceLog log = TraceLog.OpenOrConvert(parsedArgs.filePath1, null);
                SystemManagement.PrintCSV.Print(log, outFile);
            }
            Console.WriteLine("Output in " + parsedArgs.filePath2);
        }
        /// <summary>
        /// Prints a ETL or ETLX file as a XML stream of events.  Unlike 'Print' this can work on
        /// an ETL file and only does little processing of the events and no symbolic lookup.  As
        /// a result it mostly used for debuggin PerfMonitor itself.  
        /// </summary>
        private static void RawPrint(CommandLineArgs parsedArgs)
        {
            if (parsedArgs.filePath1 == null && File.Exists("PerfMonitorOutput.etl"))
                parsedArgs.filePath1 = "PerfMonitorOutput.etl";

            using (TraceEventDispatcher source = GetSource(ref parsedArgs.filePath1))
            {
                Console.WriteLine("Generating XML for " + parsedArgs.filePath1);
                Console.WriteLine("Trace duration : " + source.SessionDuration);

                if (parsedArgs.filePath2 == null)
                    parsedArgs.filePath2 = Path.ChangeExtension(parsedArgs.filePath1, ".rawPrint.xml");
                using (TextWriter output = System.IO.File.CreateText(parsedArgs.filePath2))
                {
                    KernelTraceEventParser kernelSource = source.Kernel;
                    ClrTraceEventParser clrSource = source.Clr;
                    ClrPrivateTraceEventParser clrPrivateSource = new ClrPrivateTraceEventParser(source);
                    ClrRundownTraceEventParser clrRundownSource = new ClrRundownTraceEventParser(source);
                    ClrStressTraceEventParser clrStress = new ClrStressTraceEventParser(source);
                    SymbolTraceEventParser symbolSource = new SymbolTraceEventParser(source);
                    WPFTraceEventParser wpfSource = new WPFTraceEventParser(source);

                    DynamicTraceEventParser dyamicSource = source.Dynamic;
                    if (source is ETWTraceEventSource)
                    {
                        // file names associated with disk I/O activity are logged at the end of the stream.  Thus to
                        // get file names, you need two passes
                        Console.WriteLine("PrePass to collect symbolic file name information.");
                        source.Process();
                    }

                    Console.WriteLine("Producing the XML output.");

                    output.WriteLine("<EventData>");
                    output.WriteLine(" <Header ");
                    output.WriteLine("   LogFileName=" + XmlUtilities.XmlQuote(parsedArgs.filePath1));
                    output.WriteLine("   EventsLost=" + XmlUtilities.XmlQuote(source.EventsLost));
                    output.WriteLine("   SessionStartTime=" + XmlUtilities.XmlQuote(source.SessionStartTime));
                    output.WriteLine("   SessionEndTime=" + XmlUtilities.XmlQuote(source.SessionEndTime));
                    output.WriteLine("   SessionDuration=" + XmlUtilities.XmlQuote((source.SessionDuration).ToString()));
                    output.WriteLine("   CpuSpeedMHz=" + XmlUtilities.XmlQuote(source.CpuSpeedMHz));
                    output.WriteLine("   NumberOfProcessors=" + XmlUtilities.XmlQuote(source.NumberOfProcessors));
                    output.WriteLine(" />");

                    output.WriteLine("<Events>");
                    bool dumpUnknown = parsedArgs.dumpUnknown;
                    StringBuilder sb = new StringBuilder(1024);
                    Action<TraceEvent> Printer = delegate(TraceEvent data)
                    {
                        if (dumpUnknown && data is UnhandledTraceEvent)
                            output.WriteLine(data.Dump());
                        else
                            output.WriteLine(data.ToXml(sb).ToString());
                        sb.Length = 0;
                    };
                    if (!parsedArgs.noKernel)
                        kernelSource.All += Printer;
                    if (!parsedArgs.noClr)
                    {
                        clrRundownSource.All += Printer;
                        clrPrivateSource.All += Printer;
                        clrSource.All += Printer;
                        clrStress.All += Printer;
                        wpfSource.All += Printer;
                        symbolSource.All += Printer;
                    }
                    if (!parsedArgs.noDynamic)
                        source.Dynamic.All += Printer;
                    source.UnhandledEvent += Printer;
                    source.Process();

                    output.WriteLine("</Events>");
                    output.WriteLine("</EventData>");
                    Console.WriteLine("Output in " + parsedArgs.filePath2);
                }
            }
        }
        private static void Print(CommandLineArgs parsedArgs)
        {
            if (parsedArgs.filePath1 == null)
                parsedArgs.filePath1 = "PerfMonitorOutput.etlx";

            if (parsedArgs.filePath2 == null)
                parsedArgs.filePath2 = Path.ChangeExtension(parsedArgs.filePath1, ".print.xml");

            using (StreamWriter writer = System.IO.File.CreateText(parsedArgs.filePath2))
            {
                TraceLog log = TraceLog.OpenOrConvert(parsedArgs.filePath1, null);
                TraceEvents events = log.Events;

                if (parsedArgs.processName != null)
                {
                    TraceProcess process = log.Processes.FirstProcessWithName(parsedArgs.processName);
                    if (process == null)
                        throw new ApplicationException("Could not find a process named " + parsedArgs.processName);
                    Console.WriteLine("Filtering by process " + process.ProcessID + " which started at " + process.StartTimeRelativeMsec.ToString("f2"));
                    events = process.EventsInProcess;
                }

                Console.WriteLine("Converting " + log.FilePath + " to an XML file.");
                writer.WriteLine("<TraceLog>");
                writer.WriteLine(log.ToString());
                writer.WriteLine("<Events>");
                EventStats eventStats = new EventStats();
                StringBuilder sb = new StringBuilder();
                foreach (TraceEvent anEvent in events)
                {
                    eventStats.Increment(anEvent);
                    string eventXml = anEvent.ToString();
                    if (parsedArgs.dumpUnknown && anEvent is UnhandledTraceEvent)
                        eventXml = anEvent.Dump();
                    TraceCallStack callStack = anEvent.CallStack();
                    bool opened = false;
                    if (callStack != null)
                    {
                        sb.Length = 0;
                        writer.WriteLine(XmlUtilities.OpenXmlElement(eventXml));
                        writer.WriteLine("  <StackTrace>");
                        writer.Write(callStack.ToString(sb).ToString());
                        writer.WriteLine("  </StackTrace>");
                        opened = true;
                    }
                    else
                    {
                        SampledProfileTraceData sample = anEvent as SampledProfileTraceData;
                        if (sample != null)
                        {
                            if (!opened)
                                writer.WriteLine(XmlUtilities.OpenXmlElement(eventXml));
                            opened = true;
                            writer.WriteLine(sample.IntructionPointerCodeAddressString());
                        }
                        PageFaultTraceData pageFault = anEvent as PageFaultTraceData;
                        if (pageFault != null)
                        {
                            if (!opened)
                                writer.WriteLine(XmlUtilities.OpenXmlElement(eventXml));
                            opened = true;
                            writer.WriteLine(pageFault.ProgramCounterAddressString());
                        }
                    }

                    if (opened)
                        writer.WriteLine("</Event>");
                    else
                        writer.WriteLine(eventXml);
                }

                writer.WriteLine("</Events>");

                // Write the event statistics. 
                writer.WriteLine(eventStats.ToString());

                // Dump the summary information in the log
                DumpLogData(log, writer);
                writer.WriteLine("</TraceLog>");
            }
            Console.WriteLine("Output in " + parsedArgs.filePath2);
        }
        private static void Run(CommandLineArgs parsedArgs)
        {
            if (parsedArgs.command.Length == 0)
                throw new ApplicationException("No command line given");

            // Install a Control C handler so we try to not leave the monitor running 
            int controlCPressed = 0;
            bool notNeeded = false;
            Console.CancelKeyPress += new ConsoleCancelEventHandler(delegate
            {
                if (notNeeded)
                    return;
                if (Interlocked.CompareExchange(ref controlCPressed, 1, 0) == 0)
                {
                    Console.WriteLine("Control C pressed, Stopping Monitor...");
                    parsedArgs.noEtlx = true;
                    Stop(parsedArgs);
                    Environment.Exit(1);
                }
            });

            CommandOptions options = new CommandOptions().AddTimeout(CommandOptions.Infinite);

            // before VISTA, CLR needs to be told explicitly to turn on ETW events. 
            if (Environment.OSVersion.Version.Major <= 5)
                options.AddEnvironmentVariable("COMPLUS_ETWEnabled", "1");

            Thread.Sleep(1);
            // This JITs / pages in all the code needed to do the real test, this avoids some noise. 
            Command.RunToConsole("cmd /c exit 0", options);
            Start(parsedArgs);
            bool success = false;
            try
            {
                Thread.Sleep(1000);          // Sleep a bit, so the logs can stabilize after starting up.  
                Console.WriteLine("*** Running Command: " + parsedArgs.command + " {");
                Command.RunToConsole(parsedArgs.command, options);
                Console.WriteLine("} Command Compete.");
                success = true;
            }
            finally
            {
                if (!success)
                    parsedArgs.noEtlx = true;
                Stop(parsedArgs);
                notNeeded = true;
            }
        }
        private static void RunPrint(CommandLineArgs parsedArgs)
        {
            Run(parsedArgs);
            parsedArgs.filePath1 = Path.ChangeExtension(parsedArgs.filePath1, ".etlx");
            Print(parsedArgs);
        }
        private static void List(CommandLineArgs parsedArgs)
        {
            Console.WriteLine("Active Session Names");
            foreach (string activeSessionName in TraceEventSession.GetActiveSessionNames())
                Console.WriteLine("    " + activeSessionName);
        }
        private static void Procs(CommandLineArgs parsedArgs)
        {
            if (parsedArgs.filePath1 == null)
                parsedArgs.filePath1 = "PerfMonitorOutput.etlx";

            using (TraceLog log = new TraceLog(Path.ChangeExtension(parsedArgs.filePath1, ".etlx")))
            {
                Console.WriteLine("Finding processes that were completely contained in the log " + parsedArgs.filePath1);

                TextWriter output = Console.Out;
                if (parsedArgs.filePath2 == null)
                    parsedArgs.filePath2 = "-";
                if (parsedArgs.filePath2 != "-")
                    output = System.IO.File.CreateText(parsedArgs.filePath2);

                Console.WriteLine("Trace duration : " + log.SessionDuration);

                output.WriteLine("<processes>");
                foreach (TraceProcess process in log.Processes)
                {
                    if (process.StartTime100ns > log.SessionStartTime100ns && process.EndTime100ns < log.SessionEndTime100ns)
                        output.WriteLine("  " + process.ToString());
                }
                output.WriteLine("</processes>");

                if (parsedArgs.filePath2 != "-")
                    Console.WriteLine("Output in " + parsedArgs.filePath2);
            }
        }
        private static void Merge(CommandLineArgs parsedArgs)
        {
            UnpackSupportDlls();

            if (parsedArgs.filePaths.Length == 0)
                parsedArgs.filePaths = new string[] { "PerfMonitorOutput.etl", "PerfMonitorOutput.kernel.etl" };

            if (parsedArgs.filePath1 == null)
                parsedArgs.filePath1 = Path.ChangeExtension(parsedArgs.filePaths[0], ".merged.etl");

            Console.WriteLine("Merging {0} files into file {1}", parsedArgs.filePaths.Length, parsedArgs.filePath1);
            foreach (string filePath in parsedArgs.filePaths)
                Console.WriteLine("    {0}", filePath);
            int retValue = PerfMonitorNativeMethods.CreateMergedTraceFile(
                parsedArgs.filePath1, parsedArgs.filePaths, parsedArgs.filePaths.Length,
                    PerfMonitorNativeMethods.EVENT_TRACE_MERGE_EXTENDED_DATA.IMAGEID |
                    PerfMonitorNativeMethods.EVENT_TRACE_MERGE_EXTENDED_DATA.VOLUME_MAPPING);
            if (retValue != 0)
                throw new ApplicationException("Merge operation failed.");
        }
        private static void GCTime(CommandLineArgs parsedArgs)
        {
            ProcessLookup<GCProcess> gcProcess;

            if (parsedArgs.filePath1 == null && File.Exists("PerfMonitorOutput.etl"))
                parsedArgs.filePath1 = "PerfMonitorOutput.etl";
            if (parsedArgs.filePath2 == null)
                parsedArgs.filePath2 = Path.ChangeExtension(parsedArgs.filePath1, "GCTime.xml");

            using (TraceEventDispatcher dispatcher = GetSource(ref parsedArgs.filePath1))
                gcProcess = GCProcess.Collect(dispatcher);

            using (TextWriter writer = System.IO.File.CreateText(parsedArgs.filePath2))
            {
                writer.WriteLine("<Data>");
                gcProcess.ToXml(writer, "GCProcesses");
                writer.WriteLine("</Data>");
            }
            Console.WriteLine("Log data in " + parsedArgs.filePath2);
        }
        private static void JitTime(CommandLineArgs parsedArgs)
        {
            ProcessLookup<JitProcess> jitProcess;

            if (parsedArgs.filePath1 == null && File.Exists("PerfMonitorOutput.etl"))
                parsedArgs.filePath1 = "PerfMonitorOutput.etl";
            if (parsedArgs.filePath2 == null)
                parsedArgs.filePath2 = Path.ChangeExtension(parsedArgs.filePath1, "jitTime.xml");

            using (TraceEventDispatcher dispatcher = GetSource(ref parsedArgs.filePath1))
                jitProcess = JitProcess.Collect(dispatcher);

            using (TextWriter writer = System.IO.File.CreateText(parsedArgs.filePath2))
            {
                writer.WriteLine("<Data>");
                jitProcess.ToXml(writer, "JitProcesses");
                writer.WriteLine("</Data>");
            }
            Console.WriteLine("Log data in " + parsedArgs.filePath2);
        }
        internal static void UsersGuide()
        {
            string tempDir = Environment.GetEnvironmentVariable("TEMP");
            if (tempDir == null)
                tempDir = ".";
            string helpHtmlFileName = Path.Combine(tempDir, "PerfMonitorUsersGuide.htm");
            if (!ResourceUtilities.UnpackResourceAsFile(@".\UsersGuide.htm", helpHtmlFileName))
                Console.WriteLine("No Users guide available");
            else
                new Command("\"" + helpHtmlFileName + "\"", new CommandOptions().AddStart());
        }
        private static void SetCrawlableClrStacks(bool crawlable)
        {
            // No point if not on VISTA
            if (Environment.OSVersion.Version.Major <= 5)
                return;

            // We expect PerfMonitor to run in the WOW (it is 32bit only because of its PINVOKES
            // to a 32 bit dbghelp.dll).   We want to set these keys in th WOW.  
            Debug.Assert(Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") == "x86");

            string stackKind = crawlable ? "crawlable" : "default";
            RegistryKey dotNetKey = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\.NETFramework", true);

            Console.WriteLine("Configuring the CLR JITFramed option for " + stackKind + " stacks.");
            if (crawlable)
                dotNetKey.SetValue("JITFramed", 1);
            else
                dotNetKey.DeleteValue("JITFramed", false);
            dotNetKey.Flush();

            Console.WriteLine("------------------------------------------------------------------------------");
            Console.WriteLine("Recompiling all Native code for " + stackKind + " stacks.  This may take a while...");
            Console.WriteLine();
            string ngen = Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "ngen.exe");
            Command.RunToConsole(ngen + " update /force");

            dotNetKey.Close();
        }
        private static void SetKernelStacks64(bool crawlable)
        {

            // No point if not on VISTA
            if (Environment.OSVersion.Version.Major <= 5)
                return;

            // Are we on a 64 bit system? 
            Debug.Assert(Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") == "x86");
            if (Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432") == null)
                return;

            try
            {
                RegistryKey memKey = GetMemManagementKey(true);
                if (memKey != null)
                {
                    memKey.SetValue("DisablePagingExecutive", crawlable ? 1 : 0, RegistryValueKind.DWord);
                    memKey.Close();
                    Console.WriteLine();
                    Console.WriteLine("The memory management configuration has been {0} for stack crawling.", crawlable ? "enabled" : "disabled");
                    Console.WriteLine("However a reboot is needed for it to take effect.  You can reboot by executing");
                    Console.WriteLine("     shutdown /r /t 1 /f");
                    Console.WriteLine();
                }
                else
                    Console.WriteLine("Error: Could not access Kernel memory management registry keys.");
            }
            catch (Exception e)
            {
                Console.WriteLine("Error: Failure setting registry keys: {0}", e.Message);
            }
        }
        private static bool IsKernelStacks64Enabled()
        {
            bool ret = false;
            RegistryKey memKey = GetMemManagementKey(false);
            if (memKey != null)
            {
                object valueObj = memKey.GetValue("DisablePagingExecutive", null);
                if (valueObj != null && valueObj is int)
                {
                    ret = ((int)valueObj) != 0;
                }
                memKey.Close();
            }
            return ret;
        }
        private static RegistryKey GetMemManagementKey(bool writable)
        {
            // Open this computer's registry hive remotely even though we are in th WOW we 
            // should have access to the 64 bit registry, which is what we want.
            RegistryKey hklm = RegistryKey.OpenRemoteBaseKey(RegistryHive.LocalMachine, Environment.MachineName);
            if (hklm == null)
            {
                Debug.Assert(false, "Could not get HKLM key");
                return null;
            }
            RegistryKey memManagment = hklm.OpenSubKey(@"System\CurrentControlSet\Control\Session Manager\Memory Management", writable);
            hklm.Close();
            return memManagment;
        }

        #region private
        class EventStats
        {
            public EventStats() { }
            public EventStats(TraceEventDispatcher source)
                : this()
            {
                Action<TraceEvent> StatsCollector = delegate(TraceEvent data)
                {
                    this.Increment(data);
                };

                // Add my parsers.
                source.Clr.All += StatsCollector;
                source.Kernel.All += StatsCollector;
                new ClrPrivateTraceEventParser(source).All += StatsCollector;
                new ClrRundownTraceEventParser(source).All += StatsCollector;

                source.UnhandledEvent += StatsCollector;
                source.Process();
            }

            public TaskStats this[string taskName]
            {
                get
                {
                    TaskStats ret;
                    if (!Tasks.TryGetValue(taskName, out ret))
                    {
                        ret = new TaskStats();
                        ret.Name = taskName;
                        Tasks.Add(taskName, ret);
                    }
                    return ret;
                }
            }
            public int Count;
            public int StackCount;
            public Dictionary<string, TaskStats> Tasks = new Dictionary<string, TaskStats>();
            public override string ToString()
            {
                StringBuilder sb = new StringBuilder();
                ToString(sb);
                return sb.ToString();
            }
            public void ToString(StringBuilder sb)
            {
                sb.Append("<Stats");
                sb.Append(" Count=\"").Append(Count).Append("\"");
                sb.Append(" StackCount=\"").Append(StackCount).Append("\"");
                sb.Append(">").AppendLine();

                List<TaskStats> tasks = new List<TaskStats>(Tasks.Values);
                tasks.Sort((x, y) => y.Count - x.Count);

                foreach (TaskStats task in tasks)
                    task.ToString(sb);

                sb.AppendLine("</Stats>");
            }

            internal void Increment(TraceEvent data)
            {
#if DEBUG
                Debug.Assert((byte)data.opcode != unchecked((byte)-1));        // Means PrepForCallback not done. 
                Debug.Assert(data.TaskName != "ERRORTASK");
                Debug.Assert(data.OpcodeName != "ERROROPCODE");
#endif
                Count++;
                TaskStats task = this[data.TaskName];
                if (task.ProviderName == null)
                    task.ProviderName = data.ProviderName;

                CallStackIndex index = data.CallStackIndex();
                bool hasStack = (index != CallStackIndex.Invalid);
                if (hasStack)
                    StackCount++;
                task.Increment(data.OpcodeName, hasStack);
                StackWalkTraceData asStackWalk = data as StackWalkTraceData;
                if (asStackWalk != null)
                {
                    StackWalkStats stackWalkStats = task.ExtraData as StackWalkStats;
                    if (stackWalkStats == null)
                    {
                        stackWalkStats = new StackWalkStats();
                        task.ExtraData = stackWalkStats;
                    }
                    stackWalkStats.Log(asStackWalk);
                }
            }
        }

        class StackWalkStats
        {
            public override string ToString()
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("<Stacks Count=\"").Append(stacks).Append("\"");
                sb.Append(" Frames=\"").Append(frames).Append("\"");
                if (stacks > 0)
                {
                    double average = (double)frames / stacks;
                    sb.Append(" AverageCount=\"").Append(average.ToString("f1")).Append("\"");
                }
                sb.Append(" DistictIP=\"").Append(counts.Count).Append("\"");
                sb.Append("/>").AppendLine();
                return sb.ToString();
            }
            internal unsafe void Log(StackWalkTraceData data)
            {
                stacks++;
                for (int i = 0; i < data.FrameCount; i++)
                {
                    int value = 0;
                    Address ip = data.InstructionPointer(i);
                    counts.TryGetValue((long)ip, out value);
                    value++;
                    counts[(long)ip] = value;
                    frames++;
                }
            }

            int stacks;
            int frames;
            Dictionary<long, int> counts = new Dictionary<long, int>();
        }

        class TaskStats
        {
            public void Increment(string opcodeName, bool hasStack)
            {
                if (hasStack)
                    StackCount++;
                Count++;

                OpcodeStats opcodeStats;
                if (!Opcodes.TryGetValue(opcodeName, out opcodeStats))
                {
                    opcodeStats = new OpcodeStats(opcodeName);
                    Opcodes.Add(opcodeName, opcodeStats);
                }
                opcodeStats.Increment(hasStack);
            }
            public void ToString(StringBuilder sb)
            {
                sb.Append("  <Task Name=\"").Append(Name).Append("\"");
                sb.Append(" Count=\"").Append(Count).Append("\"");
                sb.Append(" StackCount=\"").Append(StackCount).Append("\"");
                if (StackCount > 0)
                {
                    double percent = 100.0 * StackCount / Count;
                    sb.Append(" PercentWithStacks=\"").Append(percent.ToString("f1")).Append("\"");
                }
                sb.Append(" ProviderName=\"").Append(ProviderName).Append("\"");
                sb.Append(">").AppendLine();

                List<string> opcodeNames = new List<string>(Opcodes.Keys);
                opcodeNames.Sort((x, y) => Opcodes[y].Count - Opcodes[x].Count);

                foreach (string opcodeName in opcodeNames)
                    sb.Append(Opcodes[opcodeName].ToString());

                if (ExtraData != null)
                    sb.Append(ExtraData.ToString()).AppendLine();
                sb.AppendLine("  </Task>");
            }
            public string ProviderName;
            public string Name;
            public int Count;
            public int StackCount;
            public object ExtraData;
            public Dictionary<string, OpcodeStats> Opcodes = new Dictionary<string, OpcodeStats>();
        }

        class OpcodeStats
        {
            public OpcodeStats(string name)
            {
                Name = name;
            }
            public void Increment(bool hasStack)
            {
                Count++;
                if (hasStack)
                    StackCount++;
            }
            public override string ToString()
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("     <Opcode Name=\"").Append(Name).Append("\"");
                sb.Append(" Count=\"").Append(Count).Append("\"");
                sb.Append(" StackCount=\"").Append(StackCount).Append("\"");
                if (StackCount > 0)
                {
                    double percent = 100.0 * StackCount / Count;
                    sb.Append(" PercentWithStacks=\"").Append(percent.ToString("f1")).Append("\"");
                }
                sb.Append("/>").AppendLine();
                return sb.ToString();
            }
            public int Count;
            public int StackCount;
            public string Name;
        }

        private static TraceEventDispatcher GetSource(ref string filePath)
        {
            if (filePath == null)
            {
                filePath = "PerfMonitorOutput.etlx";
                if (!File.Exists(filePath))
                    filePath = "PerfMonitorOutput.etl";
            }

            bool isETLXFile = string.Compare(Path.GetExtension(filePath), ".etlx", StringComparison.OrdinalIgnoreCase) == 0;
            if (!File.Exists(filePath) && isETLXFile)
            {
                string etlFile = Path.ChangeExtension(filePath, ".etl");
                if (File.Exists(etlFile))
                {
                    filePath = etlFile;
                    isETLXFile = false;
                }
            }

            TraceEventDispatcher ret;
            if (isETLXFile)
                ret = new TraceLog(filePath).Events.GetSource();
            else
                ret = new ETWTraceEventSource(filePath);

            if (ret.EventsLost != 0)
                Console.WriteLine("WARNING: events were lost during data collection! Any anaysis is suspect!");
            return ret;
        }

        private const string UserModeSessionName = "UserModeSession";
        private static bool EnableSystemWideCLREtw(bool value, out bool changed)
        {
            changed = false;
            // None of this is needed on VISTA.  
            if (Environment.OSVersion.Version.Major > 5)
                return value;

            RegistryKey dotNetKey = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\.NETFramework", true);

            const int EnabledByPerfMonitorValue = -1;           // 'Magic' number that says perfmon set the variable
            // TODO will fail if it is not an DWORD entry
            int? regValue = (int?)dotNetKey.GetValue("ETWEnabled");

            // We don't change an exsiting value unless it is our special one whic we assume is only changed by us.  
            if (regValue != null && regValue != EnabledByPerfMonitorValue)
                value = (regValue != 0);
            else
            {
                changed = true;
                if (value)
                    dotNetKey.SetValue("ETWEnabled", EnabledByPerfMonitorValue);
                else
                    dotNetKey.DeleteValue("ETWEnabled", false);
            }
            dotNetKey.Close();
            return value;
        }

        /// <summary>
        /// To ease deployment, all support DLLs needed by perfMonitor are
        /// packed into the EXE.  This unpackes them if they have not already
        /// been unpacked.  
        /// </summary>
        private static void UnpackSupportDlls()
        {
            System.Reflection.Assembly exeAssembly = System.Reflection.Assembly.GetEntryAssembly();
            string exePath = exeAssembly.ManifestModule.FullyQualifiedName;
            DateTime exeDateUtc = File.GetLastWriteTimeUtc(exePath);
            string exeDir = Path.GetDirectoryName(exePath);
            UnpackDll(@".\dbghelp.dll", exeDir, exeDateUtc);
            UnpackDll(@".\symsrv.dll", exeDir, exeDateUtc);
            UnpackDll(@".\KernelTraceControl.dll", exeDir, exeDateUtc);
        }

        private static void UnpackDll(string resourceName, string targetDir, DateTime exeDateUtc)
        {
            // TODO be more careful about clobbering files. 
            string targetPath = Path.Combine(targetDir, resourceName);
            if (File.Exists(targetPath))
            {
                if (File.GetLastWriteTimeUtc(targetPath) > exeDateUtc)
                    return;     // Nothing to do. 
            }
            Console.WriteLine("Unpacking support dll " + targetPath);
            if (!ResourceUtilities.UnpackResourceAsFile(resourceName, targetPath))
                Console.WriteLine("Error unpacking dll!");
        }

        #endregion

        #region experimental
        private static void Memory(CommandLineArgs parsedArgs)
        {
            using (TraceLog log = new TraceLog(Path.ChangeExtension(parsedArgs.filePath1, ".etlx")))
            using (TextWriter writer = System.IO.File.CreateText(parsedArgs.filePath2))
            {
                TraceEvents events = log.Events;
                if (parsedArgs.processName != null)
                {
                    TraceProcess process = log.Processes.FirstProcessWithName(parsedArgs.processName);
                    if (process == null)
                        throw new ApplicationException("Could not find a process named " + parsedArgs.processName);
                    Console.WriteLine("Found process " + process.ProcessID);
                    events = process.EventsInProcess;
                }
                log.Kernel.VirtualAlloc += delegate(VirtualAllocTraceData eventData)
                {
                };
                log.Kernel.VirtualFree += delegate(VirtualAllocTraceData eventData)
                {
                };
            }
            Console.WriteLine("Log data in " + parsedArgs.filePath2);
        }
        private static void PrintWithStack(TextWriter writer, TraceEvent eventData)
        {
            string output = eventData.ToString();
            CallStackIndex stackIndex = eventData.CallStackIndex();
            if (stackIndex != CallStackIndex.Invalid)
            {
                writer.WriteLine(XmlUtilities.OpenXmlElement(output));
                writer.WriteLine("  <StackTrace>");
                writer.Write(eventData.CallStack().ToString());
                writer.WriteLine("  </StackTrace>");
                writer.WriteLine("</Event>");
            }
            else
            {
                SampledProfileTraceData sample = eventData as SampledProfileTraceData;
                if (sample != null)
                {
                    writer.WriteLine(XmlUtilities.OpenXmlElement(output));
                    writer.WriteLine(sample.IntructionPointerCodeAddressString());
                    writer.WriteLine("</Event>");
                }
                else
                    writer.WriteLine(output);
            }
        }

        private static bool Test_Experimental(CommandLineArgs parsedArgs)
        {
            bool ret = true;
            if (parsedArgs.filePath1 == null)
                parsedArgs.filePath1 = "PerfMonitorOutput.etl";
            else
                parsedArgs.filePath1 = Path.ChangeExtension(parsedArgs.filePath1, ".etl");
            if (parsedArgs.filePath2 == null)
                parsedArgs.filePath2 = Path.ChangeExtension(parsedArgs.filePath1, "." + parsedArgs.operation + ".xml");

            if (parsedArgs.operation == "testLog")
            {
                // TraceLog.CreateFromETL(parsedArgs.filePath1);
                using (ETWTraceEventSource source = new ETWTraceEventSource(parsedArgs.filePath1))
                {
                    TraceLog beforeLog = TraceLog.CreateFromSourceTESTONLY(source, Path.ChangeExtension(parsedArgs.filePath1, ".etlx"), null);
                    if (true)
                    {
                        Console.WriteLine("Dumping data " + parsedArgs.filePath2);
                        using (TextWriter writer = System.IO.File.CreateText(parsedArgs.filePath2))
                        {
                            writer.WriteLine("<TraceLog>");
                            DumpLogData(beforeLog, writer);
                            writer.WriteLine("</TraceLog>");
                        }
                    }
                }
            }
            else if (parsedArgs.operation == "testLogRead")
            {
                using (TraceLog log = new TraceLog(Path.ChangeExtension(parsedArgs.filePath1, ".etlx")))
                using (TextWriter writer = System.IO.File.CreateText(parsedArgs.filePath2))
                {
                    writer.WriteLine("<TraceLog>");
                    DumpLogData(log, writer);
                    writer.WriteLine("</TraceLog>");
                }
                Console.WriteLine("Log data in " + parsedArgs.filePath2);
            }
            else if (parsedArgs.operation == "testLogStackStats")
            {
                using (TraceLog log = new TraceLog(Path.ChangeExtension(parsedArgs.filePath1, ".etlx")))
                {
                    Dictionary<string, int> counts = new Dictionary<string, int>();
                    Dictionary<string, int> stackCounts = new Dictionary<string, int>();
                    foreach (TraceEvent anEvent in log.Events)
                    {
                        CallStackIndex callStackIndex = anEvent.CallStackIndex();
                        string key = anEvent.TaskName + " " + anEvent.OpcodeName;
                        int value = 0;
                        if (callStackIndex != CallStackIndex.Invalid)
                        {
                            /***
                            if (anEvent is PageFaultTraceData)
                            {
                                Console.WriteLine(anEvent.ToString());
                                Console.Write(anEvent.CallStack().ToString());
                            }
                             * **/
                            stackCounts.TryGetValue(key, out value);
                            value++;
                            stackCounts[key] = value;
                        }
                        value = 0;
                        counts.TryGetValue(key, out value);
                        value++;
                        counts[key] = value;
                    }

                    Console.WriteLine("Events with Stacks");
                    List<string> keys = new List<string>(stackCounts.Keys);
                    keys.Sort(delegate(string x, string y) { return stackCounts[y] - stackCounts[x]; });
                    foreach (string key in keys)
                        Console.WriteLine("Event " + key.PadRight(50) + " " +
                            stackCounts[key].ToString().PadLeft(8) + "  " +
                            (stackCounts[key] * 100.0 / counts[key]).ToString("f1").PadLeft(8) + "%");
                }
            }
            else if (parsedArgs.operation == "testLogEvents")
            {
                using (TraceLog log = new TraceLog(Path.ChangeExtension(parsedArgs.filePath1, ".etlx")))
                using (TextWriter writer = System.IO.File.CreateText(parsedArgs.filePath2))
                {
                    TraceEvents events = log.Events;
                    if (parsedArgs.processName != null)
                    {
                        TraceProcess process = log.Processes.FirstProcessWithName(parsedArgs.processName);
                        if (process == null)
                            throw new ApplicationException("Could not find a process named " + parsedArgs.processName);
                        Console.WriteLine("Filtering by process " + process.ProcessID + " which started at " + process.StartTimeRelativeMsec.ToString("f2"));
                        events = process.EventsInProcess;
                    }

                    Console.WriteLine("Dumping " + log.FilePath);
                    writer.WriteLine("<TraceLog>");
                    foreach (TraceEvent anEvent in events)
                    {
                        string eventXml = anEvent.ToString();
                        TraceCallStack callStack = anEvent.CallStack();
                        bool opened = false;
                        if (callStack != null)
                        {
                            writer.WriteLine(XmlUtilities.OpenXmlElement(eventXml));
                            writer.WriteLine("  <StackTrace>");
                            writer.Write(callStack.ToString());
                            writer.WriteLine("  </StackTrace>");
                            opened = true;
                        }
                        else
                        {
                            SampledProfileTraceData sample = anEvent as SampledProfileTraceData;
                            if (sample != null)
                            {
                                if (!opened)
                                    writer.WriteLine(XmlUtilities.OpenXmlElement(eventXml));
                                opened = true;
                                writer.WriteLine(sample.IntructionPointerCodeAddressString());
                            }
                        }

                        PageFaultTraceData pageFault = anEvent as PageFaultTraceData;
                        if (pageFault != null)
                        {
                            if (!opened)
                                writer.WriteLine(XmlUtilities.OpenXmlElement(eventXml));
                            opened = true;
                            writer.WriteLine(pageFault.ProgramCounterAddressString());
                        }

                        if (opened)
                            writer.WriteLine("</Event>");
                        else
                            writer.WriteLine(eventXml);
                    }
                    writer.WriteLine("</TraceLog>");
                }
                Console.WriteLine("Dumped data in " + parsedArgs.filePath2);
            }
            else if (parsedArgs.operation == "testLogBackwards")
            {
                using (TraceLog log = new TraceLog(Path.ChangeExtension(parsedArgs.filePath1, ".etlx")))
                using (TextWriter writer = System.IO.File.CreateText(parsedArgs.filePath2))
                {
                    writer.WriteLine("<TraceLog>");
                    foreach (TraceEvent anEvent in log.Events.Backwards())
                        writer.WriteLine(anEvent.ToString());
                    writer.WriteLine("</TraceLog>");
                }
                Console.WriteLine("Log data in " + parsedArgs.filePath2);
            }
            else if (parsedArgs.operation == "testLogBackwards")
            {
                using (TraceLog log = new TraceLog(Path.ChangeExtension(parsedArgs.filePath1, ".etlx")))
                using (TextWriter writer = System.IO.File.CreateText(parsedArgs.filePath2))
                {
                    writer.WriteLine("<TraceLog>");
                    foreach (TraceEvent anEvent in log.Events.Backwards())
                        writer.WriteLine(anEvent.ToString());
                    writer.WriteLine("</TraceLog>");
                }
                Console.WriteLine("Log data in " + parsedArgs.filePath2);
            }
            else if (parsedArgs.operation == "testLogBoth")
            {
                using (TraceLog log = new TraceLog(Path.ChangeExtension(parsedArgs.filePath1, ".etlx")))
                using (TextWriter writer = System.IO.File.CreateText(parsedArgs.filePath2))
                {
                    // TextWriter writer = Console.Out;

                    writer.WriteLine("<TraceLog>");
                    int j = 0;
                    foreach (ImageLoadTraceData anEvent in log.Events.ByEventType<ImageLoadTraceData>())
                    {
                        writer.WriteLine("Got " + anEvent.ToString());
                        int i = 0;
                        foreach (TraceEvent scan in log.Events.FilterByTime(anEvent.TimeStamp100ns - 1000, anEvent.TimeStamp100ns).Backwards())
                        {
                            writer.WriteLine("    PREV: " + scan.ToString());
                            i++;
                            if (i > 100)
                                break;
                        }
                        writer.WriteLine("    DONE: " + anEvent.ToString());
                        j++;
                        if (j > 200)
                            break;
                    }
                    writer.WriteLine("</TraceLog>");
                }
                Console.WriteLine("Log data in " + parsedArgs.filePath2);
            }
            else if (string.Compare(parsedArgs.operation, "CpuCallTree", StringComparison.OrdinalIgnoreCase) == 0)
            {
                if (parsedArgs.filePath2 == null)
                    parsedArgs.filePath2 = Path.ChangeExtension(parsedArgs.filePath1, ".CpuCallTree.xml");
                Console.WriteLine("Testing call stack tree");
                float thresholdPercent = 1;
                using (TraceLog log = new TraceLog(Path.ChangeExtension(parsedArgs.filePath1, ".etlx")))
                using (TextWriter writer = System.IO.File.CreateText(parsedArgs.filePath2))
                {
                    var events = log.Events;
                    if (parsedArgs.processName != null)
                    {
                        TraceProcess process = log.Processes.FirstProcessWithName(parsedArgs.processName);
                        if (process == null)
                            throw new ApplicationException("Could not find a process named " + parsedArgs.processName);
                        Console.WriteLine("Filtering by process " + process.ProcessID + " which started at " + process.StartTimeRelativeMsec.ToString("f2"));
                        events = process.EventsInProcess;
                    }

                    CallTree tree = CallTree.MethodCallTree(events.Filter(data => data is SampledProfileTraceData));
                    writer.WriteLine("<TraceStacks>");
                    tree.ToXml(writer, thresholdPercent);

                    var total = tree.Top.InclusiveMetric;
                    if (total != 0)
                    {
                        var thresholdMetric = total * thresholdPercent / 100;
                        var byMethodExclusive = tree.SumByNameSortedExclusiveMetric();
                        writer.WriteLine(" <ByMethod SortedBy=\"ExclusiveMetric\" ThresholdPercent=\"{0}\">", thresholdPercent);
                        foreach (CallTreeBaseNode node in byMethodExclusive)
                        {
                            if (node.ExclusiveMetric < thresholdMetric)
                                break;
                            writer.Write("  <Method");
                            node.ToXmlAttribs(writer);
                            writer.WriteLine("/>");
                        }
                        writer.WriteLine(" </ByMethod>", thresholdPercent);

                        thresholdPercent = Math.Min(thresholdPercent * 10, 70);
                        thresholdMetric = total * thresholdPercent / 100;
                        var byMethodInclusive = tree.SumByNameSortedInclusiveMetric();
                        writer.WriteLine(" <ByMethod SortedBy=\"InclusiveMetric\" ThresholdPercent=\"{0}\">", thresholdPercent);
                        foreach (CallTreeBaseNode node in byMethodInclusive)
                        {
                            if (node.InclusiveMetric < thresholdMetric)
                                break;
                            writer.Write("  <Method");
                            node.ToXmlAttribs(writer);
                            writer.WriteLine("/>");
                        }
                        writer.WriteLine(" </ByMethod>");

#if false
                        writer.WriteLine(" <CallerCallee>");
                        tree.CallerCallee("?!Program.Main(class System.String[])").ToXml(writer, "  ");
                        tree.CallerCallee("?!Program.RecSpin(int32)").ToXml(writer, "  ");
                        tree.CallerCallee("?!Program.SpinForASecond()").ToXml(writer, "  ");
                        tree.CallerCallee("mscorlib.ni!System.TimeZone.get_CurrentTimeZone()").ToXml(writer, "  ");
                        writer.WriteLine(" </CallerCallee>");
#endif
                    }
                    writer.WriteLine("</TraceStacks>");
                    Console.WriteLine("Call tree for threshold {0:f1} samples {1:f0} in {2}",
                        thresholdPercent, tree.Top.InclusiveCount, parsedArgs.filePath2);
                }
            }
            else if (parsedArgs.operation == "testPdb")
            {
            }
            else
                ret = false;
            return ret;
        }
        private static void DumpLogData(TraceLog log, TextWriter stream)
        {
            stream.WriteLine("  <TraceModuleFiles>");
            foreach (TraceModuleFile moduleFile in log.ModuleFiles)
            {
                stream.WriteLine("      " + moduleFile);
            }
            stream.WriteLine("  </TraceModuleFiles>");
            stream.WriteLine("<TraceProcesses>");
            foreach (TraceProcess process in log.Processes)
            {
                stream.WriteLine("  " + XmlUtilities.OpenXmlElement(process.ToString()));

                stream.WriteLine("    <TraceThreads>");
                foreach (TraceThread thread in process.Threads)
                {
                    stream.WriteLine("      " + XmlUtilities.OpenXmlElement(thread.ToString()));
                    stream.WriteLine("        </TraceThread>");
                }
                stream.WriteLine("    </TraceThreads>");

                stream.WriteLine("    <TraceLoadedModules>");
                foreach (TraceLoadedModule module in process.LoadedModules)
                {
                    TraceManagedModule asManaged = module as TraceManagedModule;
                    if (asManaged != null && asManaged.NativeModule == null)
                    {
                        stream.WriteLine("      " + XmlUtilities.OpenXmlElement(module.ToString()));
                        stream.WriteLine("      </TraceManagedModule>");
                    }
                    else
                        stream.WriteLine("      " + module);
                }
                stream.WriteLine("    </TraceLoadedModules>");
                stream.WriteLine("  </TraceProcess>");

            }
            stream.WriteLine("</TraceProcesses>");
        }
        private static void RealTime_Experimental(CommandLineArgs parsedArgs)
        {
            Console.WriteLine("Monitoring, Ctrl-C to stop");
            bool inCtrlCHander = false;

            // Start the session as a Real time monitoring session

            TraceEventSession userSession = null;
            TraceEventSession kernelSession = null;
            try
            {

                if (!parsedArgs.noClr)
                    userSession = new TraceEventSession(UserModeSessionName, null);
                if (!parsedArgs.noKernel)
                {
                    if (parsedArgs.noClr)
                        kernelSession = new TraceEventSession(KernelTraceEventParser.KernelSessionName, null);
                    else
                        Console.WriteLine("Real time kernel tracing seems to hang, only doing CLR events");
                }

                Console.CancelKeyPress += new ConsoleCancelEventHandler(delegate
                {
                    if (inCtrlCHander)
                        return;
                    inCtrlCHander = true;
                    Console.WriteLine("Stoping monitor");
                    if (userSession != null)
                    {
                        userSession.Stop();
                        userSession = null;
                    }
                    if (kernelSession != null)
                    {
                        kernelSession.Stop();
                        kernelSession = null;
                    }
                });

                Action<TraceEvent> Printer = delegate(TraceEvent data)
                {
                    if (inCtrlCHander)
                        return;

                    Console.WriteLine(data.ToString());
                    if (false && data is UnhandledTraceEvent)
                        Console.WriteLine(data.Dump());
                };

                ETWTraceEventSource kernelSource = null;
                if (kernelSession != null)
                {
                    kernelSession.EnableKernelProvider(KernelTraceEventParser.Keywords.Default);
                    kernelSource = new ETWTraceEventSource(kernelSession.SessionName, TraceEventSourceType.Session);
                }

                ETWTraceEventSource userSource = null;
                if (userSession != null)
                {
                    userSession.EnableProvider(ClrTraceEventParser.ProviderGuid, TraceEventLevel.Verbose,
                        (ulong)(ClrTraceEventParser.Keywords.Default - ClrTraceEventParser.Keywords.StopEnumeration));
                    userSource = new ETWTraceEventSource(userSession.SessionName, TraceEventSourceType.Session);
                }

                if (kernelSource != null)
                {
                    ThreadPool.QueueUserWorkItem(delegate(object obj)
                    {
                        kernelSource.Kernel.All += Printer;
                        kernelSource.UnhandledEvent += Printer;
                        kernelSource.Process();
                    });
                }
                if (userSource != null)
                {
                    ThreadPool.QueueUserWorkItem(delegate(object obj)
                    {
                        userSource.Clr.All += Printer;
                        userSource.UnhandledEvent += Printer;
                        userSource.Process();
                    });
                }

                // Flush the file every second so things get out
                ThreadPool.QueueUserWorkItem(delegate(object obj)
                {
                    for (; ; )
                    {
                        Thread.Sleep(1000);
                        Console.Out.Flush();
                    }
                });

                Thread.Sleep(Timeout.Infinite);
            }
            finally
            {
                if (userSession != null)
                    userSession.Stop();
                if (kernelSession != null)
                    kernelSession.Stop();
            }
        }
        private static void RealTimeProcs_Experimental()
        {
            Console.WriteLine("Monitoring Processes, Ctrl-C to stop");

            // Start the session as a Real time monitoring session
            TraceEventSession session = new TraceEventSession(KernelTraceEventParser.KernelSessionName, null);

            // Use Control-C to stop things.  
            Console.CancelKeyPress += new ConsoleCancelEventHandler(delegate
            {
                Console.WriteLine("Stoping tracing");
                session.Stop();
                Console.WriteLine("Done");
                Environment.Exit(0);
            });

            // OK offset collecting
            session.EnableKernelProvider(KernelTraceEventParser.Keywords.Process);

            // Start monitoring.  
            Dictionary<int, ProcessTraceData> liveProcesses = new Dictionary<int, ProcessTraceData>();
            DateTime start = DateTime.Now;

            ETWTraceEventSource source = new ETWTraceEventSource(KernelTraceEventParser.KernelSessionName, TraceEventSourceType.Session);
            source.Kernel.ProcessStart += delegate(ProcessTraceData data)
            {
                TimeSpan relativeTime = data.TimeStamp - start;
                liveProcesses[data.ProcessID] = (ProcessTraceData)data.Clone();
                Console.WriteLine(@"{0}{1}: {{ At({2}) Parent({3}) Cmd: {4}",
                    Indent(liveProcesses, data.ProcessID),
                    data.ProcessID, relativeTime, data.ParentID, data.CommandLine);
            };
            source.Kernel.ProcessEnd += delegate(ProcessTraceData data)
            {
                TimeSpan relativeTime = data.TimeStamp - start;
                ProcessTraceData startData;
                if (liveProcesses.TryGetValue(data.ProcessID, out startData))
                {
                    TimeSpan processDuration = data.TimeStamp - startData.TimeStamp;
                    Console.WriteLine("{0}{1}: }} At({2}) Exit(0x{3:x}) Duration({4}) ",
                        Indent(liveProcesses, data.ProcessID), data.ProcessID, relativeTime, data.ExitStatus, processDuration);
                }
            };
            source.Process();
            Console.WriteLine("Processing Complete");
        }


        private static string Indent(Dictionary<int, ProcessTraceData> liveProcesses, int processId)
        {
            int indent = 0;
            ProcessTraceData startData;
            while (liveProcesses.TryGetValue(processId, out startData))
            {
                processId = startData.ParentID;
                indent++;
            }
            Debug.Assert(indent > 0);
            --indent;

            return new string(' ', indent * 2);
        }


        private static void RealTimeLoads_Experimental()
        {
            Console.WriteLine("Monitoring Loads, Ctrl-C to stop");

            // Start the session as a Real time monitoring session
            TraceEventSession session = new TraceEventSession(KernelTraceEventParser.KernelSessionName, null);

            // Use Control-C to stop things.  
            Console.CancelKeyPress += new ConsoleCancelEventHandler(delegate
            {
                Console.WriteLine("Stoping tracing");
                session.Stop();
                Console.WriteLine("Done");
                Environment.Exit(0);
            });

            // OK offset collecting
            session.EnableKernelProvider(KernelTraceEventParser.Keywords.ImageLoad | KernelTraceEventParser.Keywords.Process);

            // Start monitoring.  
            ETWTraceEventSource source = new ETWTraceEventSource(KernelTraceEventParser.KernelSessionName, TraceEventSourceType.Session);

            source.Kernel.ImageLoad += delegate(ImageLoadTraceData data)
            {
                string preferred = "";
                if (data.DefaultBase != 0 && data.ImageBase != data.DefaultBase)
                    preferred = "RELOCED FROM(0x" + data.DefaultBase.ToString("x") + ")! ";
                Console.WriteLine("Process {0,5} At 0x{1,8:x} {2}Loaded {3}",
                    data.ProcessID, data.ImageBase, preferred, data.FileName);
            };
            source.Kernel.ProcessStart += delegate(ProcessTraceData data)
            {
                Console.WriteLine("Process Started {0,6} Parent {1,6} Name {2,8} Cmd: {3}",
                    data.ProcessID, data.ParentID, data.ProcessName, data.CommandLine);
            };
            source.Kernel.ProcessEnd += delegate(ProcessTraceData data)
            {
                Console.WriteLine("Process Ending {0,6} ", data.ProcessID);
            };
            source.Process();
        }
        #endregion
    }

    /// <summary>
    /// Used to generate XPerfInfo compatible Comma separated value data.  
    /// </summary>
    internal static class PrintCSV
    {
        public static void Print(TraceLog log, TextWriter outFile)
        {
            TraceEventDispatcher dispatcher = log.Events.GetSource();
            outFile.WriteLine("BeginHeader");
            outFile.WriteLine("                  Stack,  TimeStamp,   ThreadID, No.,    Address,            Image!Function");
            outFile.WriteLine("                P-Start,  TimeStamp,     Process Name ( PID),  ParentPID,  SessionID,  UniqueKey, UserSid, Command Line");
            outFile.WriteLine("                  P-End,  TimeStamp,     Process Name ( PID),  ParentPID,  SessionID,  UniqueKey,     Status, UserSid, Command Line");
            outFile.WriteLine("              P-DCStart,  TimeStamp,     Process Name ( PID),  ParentPID,  SessionID,  UniqueKey, UserSid, Command Line");
            outFile.WriteLine("                P-DCEnd,  TimeStamp,     Process Name ( PID),  ParentPID,  SessionID,  UniqueKey, UserSid, Command Line");
            outFile.WriteLine("                T-Start,  TimeStamp,     Process Name ( PID),   ThreadID,  StackBase, StackLimit, UsrStkBase,  UsrStkLmt,    TebBase, SubProcessTag,            Image!Function");
            outFile.WriteLine("                  T-End,  TimeStamp,     Process Name ( PID),   ThreadID,  StackBase, StackLimit, UsrStkBase,  UsrStkLmt,    TebBase, SubProcessTag,            Image!Function");
            outFile.WriteLine("              T-DCStart,  TimeStamp,     Process Name ( PID),   ThreadID,  StackBase, StackLimit, UsrStkBase,  UsrStkLmt,    TebBase, SubProcessTag,            Image!Function");
            outFile.WriteLine("                T-DCEnd,  TimeStamp,     Process Name ( PID),   ThreadID,  StackBase, StackLimit, UsrStkBase,  UsrStkLmt,    TebBase, SubProcessTag,            Image!Function");
            outFile.WriteLine("                I-Start,  TimeStamp,     Process Name ( PID),   BaseAddr,    EndAddr,   Checksum, TimeDateStamp,DefaultBase, FileName");
            outFile.WriteLine("                  I-End,  TimeStamp,     Process Name ( PID),   BaseAddr,    EndAddr,   Checksum, TimeDateStamp,DefaultBase, FileName");
            outFile.WriteLine("              I-DCStart,  TimeStamp,     Process Name ( PID),   BaseAddr,    EndAddr,   Checksum, TimeDateStamp,DefaultBase, FileName");
            outFile.WriteLine("                I-DCEnd,  TimeStamp,     Process Name ( PID),   BaseAddr,    EndAddr,   Checksum, TimeDateStamp,DefaultBase, FileName");
            outFile.WriteLine("                CSwitch,  TimeStamp, New Process Name ( PID),    New TID, NPri, NQnt, TmSinceLast, WaitTime, Old Process Name ( PID),    Old TID, OPri, OQnt,        OldState,      Wait Reason, Swapable, InSwitchTime, CPU, IdealProc,  OldRemQnt, NewPriDecr, PrevCState");
            outFile.WriteLine("         SampledProfile,  TimeStamp,     Process Name ( PID),   ThreadID,   PrgrmCtr, CPU, ThreadStartImage!Function, Image!Function, Count, SampledProfile type");
            outFile.WriteLine("              PageFault,  TimeStamp,     Process Name ( PID),   ThreadID,VirtualAddr,   PrgrmCtr,        Type,            Image!Function");
            outFile.WriteLine("              HardFault,  TimeStamp,     Process Name ( PID),   ThreadID,VirtualAddr,   ByteOffset,     IOSize, ElapsedTime, FileObject, FileName, Hardfaulted Address Information");
            outFile.WriteLine("           VirtualAlloc,  TimeStamp,     Process Name ( PID),   BaseAddr,    EndAddr, Flags");
            outFile.WriteLine("            VirtualFree,  TimeStamp,     Process Name ( PID),   BaseAddr,    EndAddr, Flags");
            outFile.WriteLine("               DiskRead,  TimeStamp,     Process Name ( PID),   ThreadID,     IrpPtr,   ByteOffset,     IOSize, ElapsedTime,  DiskNum, IrpFlags, DiskSvcTime, I/O Pri,  VolSnap, FileObject, FileName");
            outFile.WriteLine("              DiskWrite,  TimeStamp,     Process Name ( PID),   ThreadID,     IrpPtr,   ByteOffset,     IOSize, ElapsedTime,  DiskNum, IrpFlags, DiskSvcTime, I/O Pri,  VolSnap, FileObject, FileName");
            outFile.WriteLine("           DiskReadInit,  TimeStamp,     Process Name ( PID),   ThreadID,     IrpPtr");
            outFile.WriteLine("          DiskWriteInit,  TimeStamp,     Process Name ( PID),   ThreadID,     IrpPtr");
            outFile.WriteLine("              DiskFlush,  TimeStamp,     Process Name ( PID),   ThreadID,     IrpPtr,                           ElapsedTime,  DiskNum, IrpFlags, DiskSvcTime, I/O Pri");
            outFile.WriteLine("          DiskFlushInit,  TimeStamp,     Process Name ( PID),   ThreadID,     IrpPtr");
            outFile.WriteLine("EndHeader");
            // TODO make this completly real
            outFile.WriteLine("OS Version: 6.0.6001, Trace Size: 0KB, Events Lost: " + log.EventsLost +
                ", Buffers lost: 0, Trace Start: " + log.SessionStartTime100ns +
                " , Trace Length: " + ((int)log.SessionDuration.TotalSeconds) +
                " sec, PointerSize: " + log.PointerSize +
                ", Trace Name: " + Path.GetFileName(log.FilePath));
            outFile.WriteLine("FirstReliableEventTimeStamp, 0");

            // Put out the DCStart events.  Note that I don't bother with DCEnd events (but i could). 
            foreach (TraceProcess process in log.Processes)
            {
                if (process.StartTime100ns <= log.FirstEventTime100ns)
                    WriteProcess(outFile, "P-DCStart", process, log.SessionStartTime100ns);

                foreach (TraceThread thread in process.Threads)
                {
                    if (thread.StartTime100ns <= log.FirstEventTime100ns)
                        WriteThread(outFile, "T-DCStart", thread, log.SessionStartTime100ns);
                }

                foreach (TraceLoadedModule module in process.LoadedModules)
                {
                    if (module.LoadTime100ns <= log.FirstEventTime100ns)
                        WriteModule(outFile, "I-DCStart", module, log.SessionStartTime100ns);
                }
            }

            dispatcher.Kernel.ThreadStart += delegate(ThreadTraceData anEvent)
            {
                WriteThread(outFile, "T-Start", anEvent.Thread(), anEvent.TimeStamp100ns);
            };
            dispatcher.Kernel.ThreadEnd += delegate(ThreadTraceData anEvent)
            {
                WriteThread(outFile, "T-End", anEvent.Thread(), anEvent.TimeStamp100ns);
            };
            dispatcher.Kernel.ProcessStart += delegate(ProcessTraceData anEvent)
            {
                WriteProcess(outFile, "P-Start", anEvent.Process(), anEvent.TimeStamp100ns);
            };
            dispatcher.Kernel.ProcessEnd += delegate(ProcessTraceData anEvent)
            {
                WriteProcess(outFile, "P-End", anEvent.Process(), anEvent.TimeStamp100ns);
            };
            dispatcher.Kernel.ImageLoad += delegate(ImageLoadTraceData anEvent)
            {
                TraceLoadedModule module = anEvent.Process().LoadedModules.GetModuleContainingAddress(anEvent.ImageBase, anEvent.TimeStamp100ns);
                Debug.Assert(module != null);
                WriteModule(outFile, "I-Start", module, anEvent.TimeStamp100ns);
            };
            dispatcher.Kernel.PageFaultHardFault += delegate(PageFaultHardFaultTraceData anEvent)
            {
                WritePrefix(outFile, "HardFault", anEvent);
                WriteHex(outFile, anEvent.VirtualAddress, 11);
                outFile.Write(',');
                WriteHex(outFile, anEvent.ReadOffset, 13);
                outFile.Write(',');
                WriteHex(outFile, anEvent.ByteCount, 11);
                outFile.Write(',');
                WritePadLeft(outFile, "ElapsedTime", 12);
                outFile.Write(',');
                WriteHex(outFile, anEvent.FileObject, 11);
                outFile.Write(", ");
                WriteQuote(outFile, anEvent.FileName);      // Technically they want the kernel name. 
                outFile.Write(", ");
                WritePadLeft(outFile, "N/A", 0);
                outFile.WriteLine();
            };
            dispatcher.Kernel.AddToAllMatching(delegate(PageFaultTraceData anEvent)
            {
                string faultType = anEvent.OpcodeName;
                if (anEvent.Opcode == (TraceEventOpcode)14)    // HardFault
                    faultType = "HardFault";
                else if (faultType.EndsWith("Fault"))
                    faultType = faultType.Substring(0, faultType.Length - 5);

                WritePrefix(outFile, "PageFault", anEvent);
                WriteHex(outFile, anEvent.VirtualAddress, 11);
                outFile.Write(',');
                WriteHex(outFile, anEvent.ProgramCounter, 11);
                outFile.Write(',');
                WritePadLeft(outFile, faultType, 12);
                outFile.Write(", ");
                WriteSymolicAddress(outFile, anEvent.VirtualAddress, anEvent.ProgramCounterAddressIndex(), anEvent.Log().CodeAddresses, 27);
                outFile.WriteLine();
            });
            dispatcher.Kernel.AddToAllMatching(delegate(DiskIoTraceData anEvent)
            {
                string eventName = null;
                if (anEvent.Opcode == (TraceEventOpcode)10)             // Read
                    eventName = "DiskRead";
                else if (anEvent.Opcode == (TraceEventOpcode)11)        // Write
                    eventName = "DiskWrite";
                if (eventName != null)
                {
                    WritePrefix(outFile, eventName, anEvent);
                    WriteHex(outFile, anEvent.Irp, 11);
                    outFile.Write(',');
                    WriteHex(outFile, anEvent.ByteOffset, 13);
                    outFile.Write(',');
                    WriteHex(outFile, anEvent.TransferSize, 11);
                    outFile.Write(',');
                    WritePadLeft(outFile, "ElapsedTime", 12);
                    outFile.Write(',');
                    WritePadLeft(outFile, anEvent.DiskNumber.ToString(), 9);
                    outFile.Write(',');
                    WriteHex(outFile, anEvent.IrpFlags, 9);
                    outFile.Write(',');
                    WritePadLeft(outFile, "DiskSvcTime", 12);   // TODO We really would like this one!
                    outFile.Write(',');
                    WritePadLeft(outFile, "I/O Pri", 8);
                    outFile.Write(',');
                    WritePadLeft(outFile, "VolSnap", 9);
                    outFile.Write(',');
                    WriteHex(outFile, anEvent.FileObject, 11);
                    outFile.Write(", ");
                    WriteQuote(outFile, anEvent.FileName);      // Technically they want the kernel name. 
                    outFile.WriteLine();
                }
            });
            dispatcher.Kernel.VirtualAlloc += delegate(VirtualAllocTraceData anEvent)
            {
                WriteVirtualAlloc(outFile, anEvent, "VirtualAlloc");
            };
            dispatcher.Kernel.VirtualFree += delegate(VirtualAllocTraceData anEvent)
            {
                WriteVirtualAlloc(outFile, anEvent, "VirtualFree");
            };

            dispatcher.Kernel.PerfInfoSampleProf += delegate(SampledProfileTraceData anEvent)
            {
                WritePrefix(outFile, "SampledProfile", anEvent);
                Address address = anEvent.InstructionPointer;
                WriteHex(outFile, address, 11);
                outFile.Write(',');
                WritePadLeft(outFile, anEvent.ProcessorNumber.ToString(), 4);
                outFile.Write(", ThreadStartImage!Function,");

                CodeAddressIndex codeAddressIndex = anEvent.IntructionPointerCodeAddressIndex();
                outFile.Write(' ');
                WriteSymolicAddress(outFile, address, codeAddressIndex, anEvent.Log().CodeAddresses, 20);
                outFile.Write(", ");
                outFile.Write(anEvent.Count);
                if (anEvent.Count > 1)
                    outFile.Write(", Batched");
                else
                    outFile.Write(", Unbatched");
                outFile.WriteLine();
                WriteStack(outFile, anEvent);
            };

            Dictionary<int, long> lastRunTimes = new Dictionary<int, long>(500);

            dispatcher.Kernel.ThreadCSwitch += delegate(CSwitchTraceData anEvent)
            {
                long lastRunTime100ns;
                if (!lastRunTimes.TryGetValue(anEvent.NewThreadID, out lastRunTime100ns))
                    lastRunTime100ns = anEvent.TimeStamp100ns;
                lastRunTimes[anEvent.OldThreadID] = anEvent.TimeStamp100ns;

                WritePrefix(outFile, "CSwitch", anEvent);
                WritePadLeft(outFile, anEvent.NewThreadPriority.ToString(), 5);
                outFile.Write(',');
                WritePadLeft(outFile, anEvent.NewThreadQuantum.ToString(), 5);
                outFile.Write(',');
                WritePadLeft(outFile, ((anEvent.TimeStamp100ns - lastRunTime100ns + 5) / 10).ToString(), 12);
                outFile.Write(',');
                WritePadLeft(outFile, anEvent.NewThreadWaitTime.ToString(), 9);
                outFile.Write(',');
                WriteProcessName(outFile, anEvent.OldProcessID, anEvent.OldProcessName, 24);
                outFile.Write(',');
                WritePadLeft(outFile, anEvent.OldThreadID.ToString(), 11);
                outFile.Write(',');
                WritePadLeft(outFile, anEvent.OldThreadPriority.ToString(), 5);
                outFile.Write(',');
                WritePadLeft(outFile, anEvent.OldThreadQuantum.ToString(), 5);
                outFile.Write(',');
                WritePadLeft(outFile, anEvent.OldThreadState.ToString(), 16);
                outFile.Write(',');
                WritePadLeft(outFile, anEvent.OldThreadWaitReason.ToString(), 17);
                outFile.Write(',');
                WritePadLeft(outFile, anEvent.OldThreadWaitMode.ToString(), 9);
                outFile.Write(',');
                WritePadLeft(outFile, "InSwitchTime", 13);
                outFile.Write(',');
                WritePadLeft(outFile, anEvent.ProcessorNumber.ToString(), 4);
                outFile.Write(',');
                WritePadLeft(outFile, anEvent.OldThreadWaitIdealProcessor.ToString(), 10);
                outFile.Write(',');
                WritePadLeft(outFile, "OldRemQnt", 11);
                outFile.Write(',');
                WritePadLeft(outFile, "NewPriDecr", 11);
                outFile.Write(',');
                WritePadLeft(outFile, "PrevCState", 11);
                outFile.WriteLine();
                WriteStack(outFile, anEvent);
            };

            dispatcher.Process();
        }

        private static void WriteVirtualAlloc(TextWriter outFile, VirtualAllocTraceData anEvent, string eventName)
        {
            WritePrefix(outFile, eventName,
                 anEvent.TimeStamp100ns - anEvent.Source.SessionStartTime100ns,
                 anEvent.ProcessID, anEvent.ProcessName);
            WriteHex(outFile, anEvent.BaseAddr, 11);
            outFile.Write(',');
            WriteHex(outFile, (Address)((long)anEvent.BaseAddr + anEvent.Length), 11);
            outFile.Write(',');
            string flagsString;
            switch (anEvent.Flags)
            {
                case VirtualAllocTraceData.VirtualAllocFlags.MEM_RESERVE:
                    flagsString = "RESERVE";
                    break;
                case VirtualAllocTraceData.VirtualAllocFlags.MEM_COMMIT:
                    flagsString = "COMMIT";
                    break;
                case VirtualAllocTraceData.VirtualAllocFlags.MEM_DECOMMIT:
                    flagsString = "DECOMMIT";
                    break;
                case VirtualAllocTraceData.VirtualAllocFlags.MEM_RELEASE:
                    flagsString = "RELEASE";
                    break;
                default:
                    flagsString = ((int)anEvent.Flags).ToString();
                    break;
            }
            WritePadLeft(outFile, flagsString, 11);
            outFile.WriteLine();
        }

        private static void WriteModule(TextWriter outFile, string eventName, TraceLoadedModule module, long timeStamp100ns)
        {
            WritePrefix(outFile, eventName, timeStamp100ns - module.Process.Log.SessionStartTime100ns,
                module.Process.ProcessID, module.Process.Name);
            WriteHex(outFile, module.ImageBase, 11);
            outFile.Write(',');
            Address imageEnd = (Address)((long)module.ImageBase + module.ModuleFile.ImageSize);
            WriteHex(outFile, imageEnd, 11);
            outFile.Write(',');
            WriteHex(outFile, (Address)0, 11);  // TODO Checksum
            outFile.Write(',');
            WriteHex(outFile, (Address)0, 14);  // TODO TimeDateStamp
            outFile.Write(',');
            WriteHex(outFile, module.ModuleFile.DefaultBase, 11);
            outFile.Write(", ");
            WriteQuote(outFile, module.ModuleFile.FileName);      // Technically they want the kernel name. 
            outFile.WriteLine();
        }
        private static void WriteThread(TextWriter outFile, string eventName, TraceThread thread, long timeStamp100ns)
        {
            WritePrefix(outFile, eventName, timeStamp100ns - thread.Process.Log.SessionStartTime100ns,
                thread.Process.ProcessID, thread.Process.Name);
            WritePadLeft(outFile, thread.ThreadID.ToString(), 11);
            outFile.Write(',');
            WriteHex(outFile, (Address)0, 11);  // TODO stack base
            outFile.Write(',');
            WriteHex(outFile, (Address)0, 11);  // TODO stack limt
            outFile.Write(',');
            WriteHex(outFile, (Address)0, 11);  // TODO user base
            outFile.Write(',');
            WriteHex(outFile, (Address)0, 11);  // TODO user limt
            outFile.Write(',');
            WriteHex(outFile, (Address)0, 11);  // TODO tebBase`
            outFile.Write(',');
            WritePadLeft(outFile, "0", 14);    // TODO SubProcessTag
            outFile.Write(", UNKNOWN!UNKNOWN"); // TODO start address
            outFile.WriteLine();
        }
        private static void WriteProcess(TextWriter outFile, string eventName, TraceProcess process, long timeStamp100ns)
        {
            WritePrefix(outFile, eventName, timeStamp100ns - process.Log.SessionStartTime100ns,
                process.ProcessID, process.Name);
            WritePadLeft(outFile, process.ParentID.ToString(), 11);
            outFile.Write(',');
            WritePadLeft(outFile, "0", 11);    // TODO session Id
            outFile.Write(',');
            WriteHex(outFile, (Address)process.ProcessIndex, 11);
            outFile.Write(", UNKNOWN, ");      // TODO user SID
            outFile.Write(process.CommandLine);
            outFile.WriteLine();

        }
        private static void WriteStack(TextWriter outFile, TraceEvent anEvent)
        {
            TraceLog log = anEvent.Log();
            CallStackIndex callStackIndex = log.GetCallStackIndexForEvent(anEvent);
            TraceCallStacks callStacks = anEvent.Log().CallStacks;
            TraceCodeAddresses codeAddresses = callStacks.CodeAddresses;
            TraceMethods methods = codeAddresses.Methods;

            int i = 1;
            while (callStackIndex != CallStackIndex.Invalid)
            {
                CodeAddressIndex codeAddressIndex = callStacks.CodeAddressIndex(callStackIndex);
                Address address = codeAddresses.Address(codeAddressIndex);

                outFile.Write("                  Stack,");
                WritePadLeft(outFile, ((anEvent.TimeStamp100ns - anEvent.Source.SessionStartTime100ns) / 10).ToString(), 11);
                outFile.Write(',');
                WritePadLeft(outFile, anEvent.ThreadID.ToString(), 11);
                outFile.Write(',');
                WritePadLeft(outFile, i.ToString(), 4);
                outFile.Write(',');
                WriteHex(outFile, address, 11);
                outFile.Write(", ");

                WriteSymolicAddress(outFile, address, codeAddressIndex, codeAddresses, 0);
                outFile.WriteLine();
                callStackIndex = callStacks.Caller(callStackIndex);
                i++;
            }
        }
        private static void WritePrefix(TextWriter outFile, string eventName, TraceEvent anEvent)
        {
            WritePrefix(outFile, eventName,
                anEvent.TimeStamp100ns - anEvent.Source.SessionStartTime100ns,
                anEvent.ProcessID, anEvent.ProcessName);
            WritePadLeft(outFile, anEvent.ThreadID.ToString(), 11);
            outFile.Write(',');
        }
        private static void WritePrefix(TextWriter outFile, string eventName, long relativeTime100ns, int processId, string processName)
        {
            WritePadLeft(outFile, eventName, 23);
            outFile.Write(',');
            WritePadLeft(outFile, ((relativeTime100ns) / 10).ToString(), 11);
            outFile.Write(',');
            WriteProcessName(outFile, processId, processName, 24);
            outFile.Write(',');
        }
        private static void WriteProcessName(TextWriter outFile, int processId, string processName, int width)
        {
            string processIDStr = processId.ToString();
            int processIDLen = Math.Max(processIDStr.Length, 4);
            bool printName = (processName.IndexOf('(') < 0);

            if (printName)
                width = width - processName.Length - processIDLen - 7;
            else
                width = width - processIDLen - 2;

            WriteSpace(outFile, width);

            if (printName)
            {
                outFile.Write(processName);
                outFile.Write(".exe ");
            }
            outFile.Write("(");
            WritePadLeft(outFile, processIDStr, 4);
            outFile.Write(")");
        }
        private static void WriteSymolicAddress(TextWriter outFile, Address address, CodeAddressIndex codeAddressIndex, TraceCodeAddresses codeAddresses, int width)
        {
            MethodIndex methodIndex = codeAddresses.MethodIndex(codeAddressIndex);
            string fullMethodName = codeAddresses.Methods.FullMethodName(methodIndex);
            TraceModuleFile moduleFile = codeAddresses.ModuleFile(codeAddressIndex);

            if (moduleFile != null)
            {
                width -= moduleFile.Name.Length;
                outFile.Write(moduleFile.Name);
            }
            outFile.Write('!');
            --width;
            if (fullMethodName.Length != 0)
            {
                width -= fullMethodName.Length;
                outFile.Write(fullMethodName);
            }
            else
                width -= WriteHex(outFile, address, 0);
            WriteSpace(outFile, width);
        }
        private static int WriteHex(TextWriter outFile, int value, int width)
        {
            return WriteHex(outFile, (Address)value, width);
        }
        private static int WriteHex(TextWriter outFile, long value, int width)
        {
            return WriteHex(outFile, (Address)value, width);
        }
        private static int WriteHex(TextWriter outFile, Address address, int width)
        {
            string addressStr = ((long)address).ToString("x");
            int ret = width;
            width = width - addressStr.Length - 2;
            WriteSpace(outFile, width);
            if (width < 0)
                ret = addressStr.Length + 2;
            outFile.Write("0x");
            outFile.Write(addressStr);
            return ret;
        }
        private static void WritePadLeft(TextWriter outFile, string str, int width)
        {
            WriteSpace(outFile, width - str.Length);
            outFile.Write(str);
        }
        private static void WriteSpace(TextWriter outFile, int width)
        {
            for (int i = 0; i < width; i++)
                outFile.Write(' ');
        }
        private static void WriteQuote(TextWriter outFile, string str)
        {
            outFile.Write('"');
            if (str.IndexOf('"') < 0)
                outFile.Write(str);
            else
            {
                // TODO Not quite right for file names with quotes
                outFile.Write(str.Replace("\"", "\\\""));
            }
            outFile.Write('"');
        }
    }

    /// <summary>
    /// The code:CommandLine class holds the parsed form of all the command line arguments.  It is
    /// intialized by handing it the 'args' array for main, and it has a public field for each named argument
    /// (eg -debug). See code:#CommandLineDefinitions for the code that defines the arguments (and the help
    /// strings associated with them). 
    /// 
    /// See code:CommandLineParser for more on parser itself.   
    /// 
    /// TODO Currently not used.  Convert it its use.  
    /// </summary>
    internal class CommandLine
    {
        public CommandLine()
        {
            bool usersGuide = false;
            CommandLineParser.ParseForConsoleApplication(delegate(CommandLineParser parser)
            {
                parser.NoDashOnParameterSets = true;
                // #CommandLineDefinitions
                parser.DefineParameterSet("UsersGuide", ref usersGuide, true, "Display the users guide.");

            });
            if (usersGuide)
                PerfMonitor.UsersGuide();
        }
    };

    /// <summary>
    /// code:CommandLineArgs encapulates command line paring and help text. 
    /// * See code:CommandLineArgs#OptionsParsing for the code that sets these. 
    /// * See code:CommandLineArgs#Help for help message
    /// </summary>
    internal sealed class CommandLineArgs
    {
        public CommandLineArgs(string[] args)
        {
            providers = new List<ProviderArgs>();
            if (args.Length < 1 || args[0] == "/?")
            {
                this.help = true;
                return;
            }

            // #OptionsParsing
            // * See code:#Help for help message
            int curArg = 0;
            ProviderArgs defaultProviderArgs = new ProviderArgs(Guid.Empty); // used as a default for provider args. 
            defaultProviderArgs.Level = TraceEventLevel.Verbose;             // Our default is verbose. 
            ProviderArgs providerArgs = defaultProviderArgs;
            while (curArg < args.Length && args[curArg].StartsWith("/"))
            {
                if (string.Compare(args[curArg], "/noKernel", true) == 0)
                    this.noKernel = true;
                else if (string.Compare(args[curArg], "/cswitch", true) == 0)
                    this.kernelFlags |= KernelTraceEventParser.Keywords.ContextSwitch;
                else if (string.Compare(args[curArg], "/registry", true) == 0)
                    this.kernelFlags |= KernelTraceEventParser.Keywords.Registry;
                else if (string.Compare(args[curArg], "/noProfile", true) == 0)
                    this.kernelFlags &= ~KernelTraceEventParser.Keywords.Profile;
                else if (string.Compare(args[curArg], "/pageFault", true) == 0)
                    this.kernelFlags &= KernelTraceEventParser.Keywords.MemoryHardFaults | KernelTraceEventParser.Keywords.MemoryPageFaults;
                else if (string.Compare(args[curArg], "/noDisk", true) == 0)
                    this.kernelFlags &= ~KernelTraceEventParser.Keywords.DiskIO;
                else if (string.Compare(args[curArg], "/noNetwork", true) == 0)
                    this.kernelFlags &= ~KernelTraceEventParser.Keywords.NetworkTCPIP;
                else if (string.Compare(args[curArg], "/fileIO", true) == 0)
                    this.kernelFlags |= KernelTraceEventParser.Keywords.FileIO;
                else if (string.Compare(args[curArg], "/fileIOInit", true) == 0)
                    this.kernelFlags |= KernelTraceEventParser.Keywords.FileIOInit;
                else if (string.Compare(args[curArg], "/diskIOInit", true) == 0)
                    this.kernelFlags |= KernelTraceEventParser.Keywords.DiskIOInit;
                else if (string.Compare(args[curArg], "/virtualAlloc", true) == 0)
                    this.kernelFlags |= KernelTraceEventParser.Keywords.VirtualAlloc;
                else if (string.Compare(args[curArg], "/dispatcher", true) == 0)
                    this.kernelFlags |= KernelTraceEventParser.Keywords.Dispatcher;
                else if (string.Compare(args[curArg], "/sysCall", true) == 0)
                    this.kernelFlags |= KernelTraceEventParser.Keywords.SystemCall;
                else if (string.Compare(args[curArg], "/fileIO", true) == 0)
                    this.kernelFlags |= KernelTraceEventParser.Keywords.FileIO;
                else if (string.Compare(args[curArg], "/fileIOInit", true) == 0)
                    this.kernelFlags |= KernelTraceEventParser.Keywords.FileIOInit;
                else if (string.Compare(args[curArg], "/noClr", true) == 0)
                    this.noClr = true;
                else if (string.Compare(args[curArg], 0, "/rundownClr:", 0, 12, true) == 0)
                    this.rundownClr = int.Parse(args[curArg].Substring(12));
                else if (string.Compare(args[curArg], "/rundownClr", true) == 0)
                    this.rundownClr = 5;
                else if (string.Compare(args[curArg], "/stacks", true) == 0)
                    this.stacks = true;
                else if (string.Compare(args[curArg], "/debug", true) == 0)
                    this.debug = true;
                else if (string.Compare(args[curArg], "/verbose", true) == 0)
                    this.verbose = true;
                else if (string.Compare(args[curArg], "/dumpUnknown", true) == 0)
                    this.dumpUnknown = true;
                else if (string.Compare(args[curArg], "/noETLX", true) == 0)
                    this.noEtlx = true;
                else if (string.Compare(args[curArg], "/keepETL", true) == 0)
                    this.keepEtl = true;
                else if (string.Compare(args[curArg], "/noSym", true) == 0)
                    this.noSym = true;
                else if (string.Compare(args[curArg], "/symDebug", true) == 0)
                    this.symDebug = true;
                else if (string.Compare(args[curArg], "/lineNumbers", true) == 0)
                    this.lineNumbers = true;
                else if (string.Compare(args[curArg], 0, "/dlls:", 0, 6, true) == 0)
                    this.dlls = args[curArg].Substring(6).Split(',');
                else if (string.Compare(args[curArg], 0, "/out:", 0, 5, true) == 0)
                    this.filePath1 = args[curArg].Substring(5);
                else if (string.Compare(args[curArg], 0, "/proc:", 0, 6, true) == 0)
                    this.processName = args[curArg].Substring(6);
                else if (string.Compare(args[curArg], "/noDynamic", true) == 0)
                    this.noDynamic = true;
                else if (string.Compare(args[curArg], 0, "/provider:", 0, 10, true) == 0)
                {
                    string providerSpec = args[curArg].Substring(10);
                    if (providerSpec.StartsWith("@"))
                    {
                        int atIndex = providerSpec.IndexOf('#', 1);
                        string providerName = null;
                        if (atIndex < 0)
                            atIndex = providerSpec.Length;
                        else
                            providerName = providerSpec.Substring(atIndex + 1);

                        bool anyProviders = false;
                        string fileName = providerSpec.Substring(1, atIndex - 1);
                        if (!File.Exists(fileName))
                        {
                            var exe = fileName + ".exe";
                            if (File.Exists(exe))
                                fileName = exe;
                            else
                            {
                                var dll = fileName + ".dll";
                                if (File.Exists(dll))
                                    fileName = dll;
                            }
                        }

                        // Console.WriteLine("Got Provider Name '{0}'", providerName);
                        // Console.WriteLine("Got File Name '{0}'", fileName);
                        foreach (Type eventSource in GetEventSourcesInFile(fileName))
                        {
                            bool useProvider = false;
                            if (providerName == null)
                                useProvider = true;
                            else
                            {
                                string eventSourceName = GetName(eventSource);
                                if (String.Compare(providerName, eventSourceName, StringComparison.OrdinalIgnoreCase) == 0)
                                    useProvider = true;
                                else
                                {
                                    int dot = eventSourceName.LastIndexOf('.');
                                    if (dot >= 0)
                                        eventSourceName = eventSourceName.Substring(dot + 1);

                                    if (String.Compare(providerName, eventSourceName, StringComparison.OrdinalIgnoreCase) == 0)
                                        useProvider = true;
                                    else
                                    {
                                        if (eventSourceName.EndsWith("EventSource", StringComparison.OrdinalIgnoreCase))
                                            eventSourceName = eventSourceName.Substring(0, eventSourceName.Length - 11);

                                        if (String.Compare(providerName, eventSourceName, StringComparison.OrdinalIgnoreCase) == 0)
                                            useProvider = true;

                                    }
                                }
                            }
                            if (useProvider)
                            {
                                anyProviders = true;
                                Console.WriteLine("Found Provider {0} Guid {1}", GetName(eventSource), GetGuid(eventSource));
                                providerArgs = new ProviderArgs(GetGuid(eventSource), defaultProviderArgs);
                                providers.Add(providerArgs);
                            }
                        }
                        if (!anyProviders)
                        {
                            if (providerName != null)
                                throw new ApplicationException("EventSource " + providerName + " not found in " + fileName);
                            else
                                throw new ApplicationException("No types deriving from EventSource found in " + fileName);
                        }
                    }
                    else
                    {
                        Guid providerGuid;
                        if (Regex.IsMatch(providerSpec, "........-....-....-....-............"))
                        {
                            try
                            {
                                providerGuid = new Guid(providerSpec);
                            }
                            catch
                            {
                                throw new ApplicationException("Could not parse Guid '" + providerSpec + "'");
                            }
                        }
                        else if (string.Compare(providerSpec, "Clr", StringComparison.OrdinalIgnoreCase) == 0)
                            providerGuid = ClrTraceEventParser.ProviderGuid;
                        else if (string.Compare(providerSpec, "ClrRundown", StringComparison.OrdinalIgnoreCase) == 0)
                            providerGuid = ClrRundownTraceEventParser.ProviderGuid;
                        else if (string.Compare(providerSpec, "ClrStress", StringComparison.OrdinalIgnoreCase) == 0)
                            providerGuid = ClrStressTraceEventParser.ProviderGuid;
                        else if (string.Compare(providerSpec, "Wpf", StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            providerGuid = WPFTraceEventParser.ProviderGuid;
                            // TODO this is less than ideal in that it changes the default for other providers.  
                            if (providerArgs.Level == 0)
                                providerArgs.Level = TraceEventLevel.Verbose;
                        }
                        else
                            throw new ApplicationException("Could not find provider name '" + providerSpec + "'");

                        providerArgs = new ProviderArgs(providerGuid, defaultProviderArgs);
                        providers.Add(providerArgs);
                    }
                }
                else if (string.Compare(args[curArg], 0, "/source:", 0, 8, true) == 0)
                {
                    string sourceName = args[curArg].Substring(8);
                    var providerGuid = GenerateGuidFromName(sourceName.ToLowerInvariant());
                    providerArgs = new ProviderArgs(providerGuid, defaultProviderArgs);
                    providers.Add(providerArgs);
                }
                else if (string.Compare(args[curArg], 0, "/level:", 0, 7, true) == 0)
                {
                    providerArgs.Level = (TraceEventLevel)int.Parse(args[curArg].Substring(7));
                    if (!(TraceEventLevel.Always <= providerArgs.Level && providerArgs.Level <= TraceEventLevel.Verbose))
                        throw new ApplicationException("TraceEventLevel must be in the range 0 (Always) to 5 (Verbose)");
                }
                else if (string.Compare(args[curArg], 0, "/keyAny:", 0, 8, true) == 0)
                {
                    providerArgs.MatchAnyKeywords = ParseKeywords(providerArgs.Guid, args[curArg].Substring(8));
                }
                else if (string.Compare(args[curArg], 0, "/keyAll:", 0, 8, true) == 0)
                {
                    providerArgs.MatchAnyKeywords = ParseKeywords(providerArgs.Guid, args[curArg].Substring(8));
                }
                else if (string.Compare(args[curArg], 0, "/data:", 0, 6, true) == 0)
                {
                    string dataSpec = args[curArg].Substring(6);
                    int equalsIdx = dataSpec.IndexOf('=');
                    if (equalsIdx <= 0)
                        throw new ApplicationException("/data value '" + dataSpec + "' is not of form key=value");
                    string key = dataSpec.Substring(0, equalsIdx);
                    string value = dataSpec.Substring(equalsIdx + 1);

                    if (providerArgs.providerData == null)
                        providerArgs.providerData = new Dictionary<string, string>();
                    providerArgs.providerData[key] = value;
                }
                else
                    throw new ApplicationException("Unrecognized option " + args[curArg]);
                curArg++;
            }

            if (Environment.OSVersion.Version.Major <= 5 && (kernelFlags & (KernelTraceEventParser.Keywords.Profile | KernelTraceEventParser.Keywords.ProcessCounters)) != KernelTraceEventParser.Keywords.None)
            {
                kernelFlags &= ~(KernelTraceEventParser.Keywords.Profile | KernelTraceEventParser.Keywords.ProcessCounters);
                Console.WriteLine("Warning: sample profiling specified but unsupported on Vista, disabling.");
            }

            if (!(curArg < args.Length))
                throw new ApplicationException("No operations given");
            this.operation = args[curArg++];
            if (this.operation.StartsWith("run", StringComparison.OrdinalIgnoreCase))
            {
                this.command = CommandLineUtilities.FormCommandLineFromArguments(args, curArg);
            }
            else if (string.Compare(this.operation, "merge", StringComparison.OrdinalIgnoreCase) == 0)
            {
                this.filePaths = new string[args.Length - curArg];
                Array.Copy(args, curArg, filePaths, 0, filePaths.Length);
            }
            else
            {
                if (curArg < args.Length)
                    this.filePath1 = args[curArg++];
                if (curArg < args.Length)
                    this.filePath2 = args[curArg++];
            }
            if (!this.noClr)
            {
                if (!HasProvider(providers, ClrTraceEventParser.ProviderGuid))
                {
                    ProviderArgs clrProviderArgs = new ProviderArgs(ClrTraceEventParser.ProviderGuid);
                    clrProviderArgs.MatchAnyKeywords = (ulong)ClrTraceEventParser.Keywords.Default;
                    if (this.stacks)
                        clrProviderArgs.MatchAnyKeywords |= (ulong)ClrPrivateTraceEventParser.Keywords.Stack;
                    clrProviderArgs.Level = TraceEventLevel.Verbose;
                    providers.Add(clrProviderArgs);
                }

                if (!HasProvider(providers, ClrRundownTraceEventParser.ProviderGuid))
                {
                    ProviderArgs clrRundownProviderArgs = new ProviderArgs(ClrRundownTraceEventParser.ProviderGuid);
                    clrRundownProviderArgs.MatchAnyKeywords = (ulong)(
                        ClrTraceEventParser.Keywords.StopEnumeration |
                        ClrTraceEventParser.Keywords.NGen |
                        ClrTraceEventParser.Keywords.Jit |
                        ClrTraceEventParser.Keywords.Loader);
                    clrRundownProviderArgs.Level = TraceEventLevel.Verbose;
                    providers.Add(clrRundownProviderArgs);
                }
            }
        }

        public static void ProviderHelp(CommandLineArgs parsedArgs)
        {
            Console.WriteLine("Each event in a provider has two attributes that determine if it is active.");
            Console.WriteLine("");
            Console.WriteLine("    1) A 'level' from 0 (always) through 5 (verbose)");
            Console.WriteLine("    2) A 64 bit bitvector representing its 'keywords'");
            Console.WriteLine("");
            Console.WriteLine("When specifying providers for the 'start' command you can also specify");
            Console.WriteLine("");
            Console.WriteLine("    1) A /level:<level> ");
            Console.WriteLine("    2) A /keyAny:<keywords>");
            Console.WriteLine("    3) A /keyAll:<keywords>");
            Console.WriteLine("");
            Console.WriteLine("Only events that are whose level is less than or equal to the specified level");
            Console.WriteLine("AND whose keywords intesect with the /keyAny bitvector");
            Console.WriteLine("AND whose keywords are a complete superset of the /keyAll bitvector");
            Console.WriteLine("");
            Console.WriteLine("The keywords can be specified either as a sum of hex numbers or well known");
            Console.WriteLine("symbolic identifiers for the keywords.  For example");
            Console.WriteLine("");
            Console.WriteLine("    perfMonitor /provider:clr /keyAny:Default+JitTracing start");
            Console.WriteLine("");
            Console.WriteLine("The following is a list of know providers and keywords. ");
            Console.WriteLine("");
            foreach (Type parser in s_Parsers)
            {
                object name = parser.GetField("ProviderName").GetValue(null);
                object guid = parser.GetField("ProviderGuid").GetValue(null);
                Type keywordType = (Type)(parser.GetMember("Keywords")[0]);
                string[] keywords = Enum.GetNames(keywordType);

                Console.WriteLine("Provider {0}", name);
                Console.WriteLine("    Guid {0}", guid);
                Console.WriteLine("    Keywords");
                foreach (var keyword in keywords)
                {
                    Console.WriteLine("        {0,-32} 0x{1:x}", keyword, Enum.Parse(keywordType, keyword));
                }
            }
        }

        private static Type[] s_Parsers = new Type[] { typeof(KernelTraceEventParser), typeof(ClrTraceEventParser) };
        /// <summary>
        /// parse the keywords for a give provider (always allowing a hex number as a fallback)
        /// </summary>
        private static ulong ParseKeywords(Guid providerGuid, string keyString)
        {
            ulong ret = 0;
            Type keywordEnum = null;
            string[] keywordEnumNames = null;
            foreach (Type parser in s_Parsers)
            {
                object parserGuid = parser.GetField("ProviderGuid").GetValue(null);
                if (providerGuid.Equals(parserGuid))
                    keywordEnum = (Type)(parser.GetMember("Keywords")[0]);
            }
            int endIdx;
            char[] operators = new char[] { '+', '-' };
            bool negate = false;
            for (int curIdx = 0; curIdx <= keyString.Length; curIdx = endIdx + 1)
            {
                endIdx = keyString.IndexOfAny(operators, curIdx);
                if (endIdx < 0)
                    endIdx = keyString.Length;

                // This is one term of the bitvector
                var term = keyString.Substring(curIdx, endIdx - curIdx);

                // See if it is a symbolic keyword
                ulong value;
                if (keywordEnum != null)
                {
                    if (keywordEnumNames == null)
                        keywordEnumNames = Enum.GetNames(keywordEnum);
                    foreach (var keywordEnumName in keywordEnumNames)
                    {
                        if (string.Compare(term, keywordEnumName, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            value = (ulong)(long)Enum.Parse(keywordEnum, keywordEnumName);
                            goto FoundTerm;
                        }
                    }
                }
                // Allow 0x prefix (but it is redundant) as we always assume it.  
                if (term.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    term = term.Substring(2);

                // Parse as a hex string. 
                if (!ulong.TryParse(term, System.Globalization.NumberStyles.HexNumber, null, out value))
                    throw new ApplicationException("The string " + term + " is not a hex number or a known keyword.");
            FoundTerm:
                if (negate)
                    ret &= ~value;
                else
                    ret |= value;
                if (endIdx < keyString.Length && keyString[endIdx] == '-')
                    negate = true;
            }
            return ret;
        }

        public static string UsageHelpText()
        {
            // #Help  
            // * See code:#OptionsParsing for where these are parsed. 
            // * See code:CommandLineArgs for structure that embodies all the parsed args. 
            string ret =
                "\r\n" +
                "PerfMonitor is a tool for collecting and accessing windows tracing information\r\n" +
                "(aka Event Tracing for Windows or ETW).  Built into many parts of windows \r\n" +
                "including the kernel and the .NET runtime are logging routines that will \r\n" +
                "generate very detailed information about what the computer is doing.   \r\n" +
                "PerfMonitor allows you to turn this logging on (on a machine wide basis) and\r\n" +
                "collect the data into Event Trace Log (ETL) files.   PerfMonitor also allows \r\n" +
                "you to convert the binary ETL files to an XML file.\r\n" +
                "\r\n" +
                "Usage: PerfMonitor providerHelp                  Help specific to providers.\r\n" +
                "       PerfMonitor [options] start <file>        Starts a monitoring of a file.\r\n" +
                "       PerfMonitor [options] list                Displays active sessions.\r\n" +
                "       PerfMonitor [options] stop                Stops a monitoring session.\r\n" +
                "       PerfMonitor [options] listSources <exe>   Lists eventSources in an exe.\r\n" +
                "       PerfMonitor [options] print <fIn> [<fOut>] Print ETLX <fIn> to XML.\r\n" +
                "       PerfMonitor [options] rawPrint <fIn> [<fOut>] Print ETL <fIn> to XML.\r\n" +
                "       PerfMonitor [options] printCSV <fIn> [<fOut>] Prints ETLX to CSV file.\r\n" +
                "       PerfMonitor [options] stats <fIn> [<fOut>] Computes counts for <fin>.\r\n" +
                "       PerfMonitor [options] procs <fIn> [<fOut>] Shows processes for <fin>.\r\n" +
                "       PerfMonitor [options] convert <fIn> [<fOut>] Converts an ETL to ETLX.\r\n" +
                "       PerfMonitor [/out:<file>] merge <files>   Merge etl files to a one file.\r\n" +
                "\r\n" +
                "       PerfMonitor usersGuide                    Show complete users guide.\r\n" +
                "\r\n" +
                "       PerfMonitor [options] run <cmd>           Starts, runs, and stops.\r\n" +
                "       PerfMonitor [options] runPrint <cmd>      Runs then prints.\r\n" +
                "\r\n" +
                "       PerfMonitor crawlableCLRStacks            Configure crawlable CLR stacks.\r\n" +
                "       PerfMonitor defaultCLRStacks              Configure default CLR stacks.\r\n" +
                "       PerfMonitor enableKernelStacks64          Config kernel stacks (64 bit).\r\n" +
                "       PerfMonitor disableKernelStacks64         Unconfig kernel stacks (64 bit)\r\n" +
                "\r\n" +
                "Typical Usages:\r\n" +
                "       PerfMonitor runPrint <cmd>           start, runs cmd, stops, prints\r\n" +
                "\r\n" +
                "       PerfMonitor start                    Starts data collection\r\n" +
                "       [run scenario]                       Run interesting scenario.\r\n" +
                "       PerfMonitor stop                     Stops data collection.\r\n" +
                "       PerfMonitor print                    Converts *.etl file to *.xml.\r\n" +
                "\r\n" +
                "If file names are not given, perfMonitor.etl is assumed.   For\r\n" +
                "'PerfMonitor print' the name - means console output.\r\n" +
                "\r\n" +
                "Options for: run, runPrint, start, print:\r\n" +
                "    /noKernel:      Don't monitor kernel events.\r\n" +
                "    /noClr:         Don't monitor clr events.\r\n" +
                "    /provider:<spec> Turn on an additiona provider.\r\n" +
                "                    <spec> can be guid, name or @file#name.\r\n" +
                "    /source:<name>  Turn on a managed event source.\r\n" +
                "  Options that apply to the preceeding /provider qualifer\r\n" +
                "  Those before any provider are a default for all providers\r\n" +
                "    /level:<num>    Set provider verbosity: 0 = default 5 = verbose\r\n" +
                "    /keyAny:<hex>   Message that set any of these bits are on.\r\n" +
                "    /keyAll:<hex>   Only messages that set all these bits are on.\r\n" +
                "    /data:<key>=<data> Additional data sent to provider. (multiple allowed)\r\n" +
                "Options for: stop\r\n" +
                "    /rundownClr[:n] Force CLR to dump symbolic information before detaching\r\n" +
                "                    'n' is the number of seconds to wait for the rundown.\r\n" +
                "                    the default is 5 sec.\r\n" +
                "                    Useful when CLR process has not terminated.\r\n" +
                "Options for: run, runPrint, start:\r\n" +
                "    /noProfile:     Turn off sample based profiling.\r\n" +
                "    /noDisk:        Turn off disk I/O profiling.\r\n" +
                "    /noNetwork:     Turn off network profiling.\r\n" +
                "    /pageFault:     Turn On page fault profiling.\r\n" +
                "    /diskIOInit:    Show disk I/O inits as well as completes.\r\n" +
                "    /registry:      Show accesses to the registry.\r\n" +
                "    /syscall:       Show os system calls (large).\r\n" +
                "    /cSwitch:       Show context switch events.\r\n" +
                "    /virtualAlloc:  Show virtual memory allocation events.\r\n" +
                "    /fileIO:        Show file operations (just completes).\r\n" +
                "    /fileIOInit:    Show file I/O inits as well as completes.\r\n" +
                "    /stacks:        Capture stack traces with events.\r\n" +
                "Options for: run, runPrint\r\n" +
                "    /out:<file>     Use file as basename for output.\r\n" +
                "    /dumpUnknown    Dump the data bytes of unknown events\r\n" +
                "Options for symbol lookup (perfMonitor convert)\r\n" +
                "    /dlls:<DllList> Comma separated list of dlls names (without\r\n" +
                "                    file exention) that should have their symbols\r\n" +
                "                    fetched. (default: ntdll, kernel32, advapi32),\r\n" +
                "    /noSym          Clear the default set of dlls for symbol fetch\r\n" +
                "    /lineNumbers    Look up line numbers as well as method names\r\n" +
                // TODO keep?                "    /symDebug       RawPrint verbose output on symbol resolution\r\n" + 
                "Options for: stop, run, runPrint\r\n" +
                "    /noETLX         Don't generate a ETLX file\r\n" +
                "    /keepETL        Keep the ETL files after conversion\r\n" +
                "\r\n" +
                "Use: 'perfMonitor usersGuide' for a complete users guide.\r\n" +
                "Use: 'perfMonitor providerHelp' for help setting provider keywords.\r\n" +
                "";
            return ret;
        }

        internal class ProviderArgs
        {
            public ProviderArgs(Guid guid) { Guid = guid; }
            public ProviderArgs(Guid guid, ProviderArgs template)
            {
                Guid = guid;
                MatchAnyKeywords = template.MatchAnyKeywords;
                MatchAllKeywords = template.MatchAllKeywords;
                Level = template.Level;
                providerData = template.providerData;
            }
            public Guid Guid;
            public ulong MatchAnyKeywords;
            public ulong MatchAllKeywords;
            public TraceEventLevel Level;
            public Dictionary<string, string> providerData;   // optional data to send to provider
        };

        // options 
        internal bool noDynamic;
        internal bool noKernel;
        internal bool stacks;
        internal bool noClr;
        internal int rundownClr;
        internal bool debug;
        internal bool verbose;
        internal bool dumpUnknown;
        internal bool noEtlx;
        internal bool keepEtl;
        internal bool lineNumbers;
        internal bool noSym;
        internal bool symDebug;
        internal string[] dlls;
        internal bool help;
        internal KernelTraceEventParser.Keywords kernelFlags = KernelTraceEventParser.Keywords.Default;
        internal List<ProviderArgs> providers;     // Optional additional providers to activate.  

        internal string processName;            // If you can filter by process, this is the filter. 

        // args that are not options.  
        internal string operation;          // The first argument that is not a option see code:Program#OperationParsing
        internal string filePath1;          // 1st Argument after operation, null if not present.
        internal string filePath2;          // 2nd Argument after operation, null if not present.

        internal string[] filePaths;        // used for merge.

        internal string command;            // All args after command concatinated as one string.  

        #region private
        // TODO remove and depend on framework for these instead.  
        internal static Guid GetGuid(Type eventSource)
        {
            return GenerateGuidFromName(GetName(eventSource).ToLowerInvariant());
        }
        internal static string GetName(Type eventSource)
        {
            // TODO not correct, does not work for types given a name using explict attributes, fix when
            // becomes part of the framework.
            return eventSource.FullName;
        }
        internal static Guid GenerateGuidFromName(string name)
        {
            // The algorithm below is following the guidance of http://www.ietf.org/rfc/rfc4122.txt
            // Create a blob containing a 16 byte number representing the namespace
            // followed by the unicode bytes in the name.  
            var bytes = new byte[name.Length * 2 + 16];
            uint namespace1 = 0x482C2DB2;
            uint namespace2 = 0xC39047c8;
            uint namespace3 = 0x87F81A15;
            uint namespace4 = 0xBFC130FB;
            // Write the bytes most-significant byte first.  
            for (int i = 3; 0 <= i; --i)
            {
                bytes[i] = (byte)namespace1;
                namespace1 >>= 8;
                bytes[i + 4] = (byte)namespace2;
                namespace2 >>= 8;
                bytes[i + 8] = (byte)namespace3;
                namespace3 >>= 8;
                bytes[i + 12] = (byte)namespace4;
                namespace4 >>= 8;
            }
            // Write out  the name, most significant byte first
            for (int i = 0; i < name.Length; i++)
            {
                bytes[2 * i + 16 + 1] = (byte)name[i];
                bytes[2 * i + 16] = (byte)(name[i] >> 8);
            }

            // Compute the Sha1 hash 
            var sha1 = System.Security.Cryptography.SHA1.Create();
            byte[] hash = sha1.ComputeHash(bytes);

            // Create a GUID out of the first 16 bytes of the hash (SHA-1 create a 20 byte hash)
            int a = (((((hash[3] << 8) + hash[2]) << 8) + hash[1]) << 8) + hash[0];
            short b = (short)((hash[5] << 8) + hash[4]);
            short c = (short)((hash[7] << 8) + hash[6]);

            //TODO review big-endian little-endian issues.  
            c = (short)((c & 0x0FFF) | 0x3000);   // Set high 4 bits of octet 7 to 3, as per RFC 4122
            Guid guid = new Guid(a, b, c, hash[8], hash[9], hash[10], hash[11], hash[12], hash[13], hash[14], hash[15]);
            return guid;
        }

        private bool HasProvider(List<ProviderArgs> providers, Guid guid)
        {
            foreach (ProviderArgs provider in providers)
                if (provider.Guid == guid)
                    return true;
            return false;
        }

        // TODO load it its own appdomain so we can unload them properly.
        internal static IEnumerable<Type> GetEventSourcesInFile(string fileName)
        {
            // TODO follow static dependencies. 
            System.Reflection.Assembly assembly;
            try
            {
                assembly = System.Reflection.Assembly.ReflectionOnlyLoadFrom(fileName);
            }
            catch (Exception e)
            {
                // Convert to an application exception TODO is this a good idea?
                throw new ApplicationException(e.Message);
            }

            Dictionary<Assembly, Assembly> soFar = new Dictionary<Assembly, Assembly>();
            GetStaticReferencedAssemblies(assembly, soFar);

            List<Type> eventSources = new List<Type>();
            foreach (Assembly subAssembly in soFar.Keys)
            {
                foreach (Type type in subAssembly.GetTypes())
                {
                    if (type.BaseType != null && type.BaseType.Name == "EventSource")
                        eventSources.Add(type);
                }
            }
            return eventSources;
        }

        private static void GetStaticReferencedAssemblies(Assembly assembly, Dictionary<Assembly, Assembly> soFar)
        {
            soFar[assembly] = assembly;
            string assemblyDirectory = Path.GetDirectoryName(assembly.ManifestModule.FullyQualifiedName);
            foreach (AssemblyName childAssemblyName in assembly.GetReferencedAssemblies())
            {
                try
                {
                    // TODO is this is at best heuristic.  
                    string childPath = Path.Combine(assemblyDirectory, childAssemblyName.Name + ".dll");
                    Assembly childAssembly = null;
                    if (File.Exists(childPath))
                        childAssembly = Assembly.ReflectionOnlyLoadFrom(childPath);

                    //TODO do we care about things in the GAC?   it expands the search quite a bit. 
                    //else
                    //    childAssembly = Assembly.Load(childAssemblyName);

                    if (childAssembly != null && !soFar.ContainsKey(childAssembly))
                        GetStaticReferencedAssemblies(childAssembly, soFar);
                }
                catch (Exception)
                {
                    Console.WriteLine("Could not load assembly " + childAssemblyName + " skipping.");
                }
            }
        }
        #endregion
    }
}

