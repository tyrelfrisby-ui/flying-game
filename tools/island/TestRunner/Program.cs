using System.Reflection;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

// Uses xUnit's own in-process framework, preserving fixtures, collections, data discovery,
// assertions and skip handling. No socket-based VSTest host; no test sources changed.
var sink = new Results();
using ITestFramework framework = new XunitTestFramework(sink);
var executor = framework.GetExecutor(Assembly.Load("FlightTests").GetName());
var discovery = TestFrameworkOptions.ForDiscovery();
var execution = TestFrameworkOptions.ForExecution();
execution.SetValue("xunit.execution.DisableParallelization", true);
execution.SetValue("xunit.execution.MaxParallelThreads", 1);
if(args.Length==0) executor.RunAll(sink, discovery, execution);
else
{
    var cases=new Cases();framework.GetDiscoverer(new ReflectionAssemblyInfo(Assembly.Load("FlightTests"))).Find(false,cases,discovery);
    if(!cases.Done.Wait(TimeSpan.FromMinutes(2)))throw new TimeoutException("discovery");
    executor.RunTests(cases.All.Where(c=>args.Any(a=>c.DisplayName.Contains(a))),sink,execution);
}
if (!sink.Done.Wait(TimeSpan.FromMinutes(30))) throw new TimeoutException("xUnit execution timed out");
Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { sink.Passed, sink.Failed, sink.Skipped, sink.Failures }));
Environment.ExitCode = sink.Failed > 0 ? 1 : 0;
sealed class Results : IMessageSink
{
    public readonly ManualResetEventSlim Done = new();
    public int Passed, Failed, Skipped;
    public readonly List<object> Failures = new();
    public bool OnMessage(IMessageSinkMessage message)
    {
        if (message is ITestPassed) { int n=Interlocked.Increment(ref Passed); if(n%100==0) Console.Error.WriteLine("PROGRESS "+n+" passed"); }
        if (message is ITestSkipped) Interlocked.Increment(ref Skipped);
        if (message is ITestFailed fail)
        {
            Interlocked.Increment(ref Failed);
            lock (Failures) Failures.Add(new { name = fail.Test.DisplayName, messages = fail.Messages, stack = fail.StackTraces });
            Console.Error.WriteLine("FAIL " + fail.Test.DisplayName + " " + string.Join("; ", fail.Messages));
        }
        if (message is IErrorMessage error) Console.Error.WriteLine("ERROR " + string.Join("; ", error.Messages));
        if (message is ITestAssemblyFinished) Done.Set();
        return true;
    }
}

sealed class Cases:IMessageSink
{
    public readonly List<ITestCase> All=new();public readonly ManualResetEventSlim Done=new();
    public bool OnMessage(IMessageSinkMessage m) {if(m is ITestCaseDiscoveryMessage c)All.Add(c.TestCase);if(m is IDiscoveryCompleteMessage)Done.Set();return true;}
}
