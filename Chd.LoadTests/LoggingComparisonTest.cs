using System.Reflection;
using Castle.DynamicProxy;
using Chd.Logging.Enums;
using Newtonsoft.Json;
using NBomber.CSharp;
using Serilog;
using Serilog.Events;
using Serilog.Parsing;

namespace Chd.LoadTests;

public interface ILoggedSum
{
    int Sum(int a, int b);
}

public interface ILoggedFailure
{
    int Fail(int a, int b);
}

public sealed class PlainSum : ILoggedSum
{
    public Microsoft.AspNetCore.Http.HttpContext? HttpContext { get; set; }

    public int Sum(int a, int b) => a + b;
}

public sealed class PlainFailure : ILoggedFailure
{
    public Microsoft.AspNetCore.Http.HttpContext? HttpContext { get; set; }

    public int Fail(int a, int b) => throw new InvalidOperationException("sum failed");
}

public sealed class ChdLoggedSum
{
    public Microsoft.AspNetCore.Http.HttpContext? HttpContext { get; set; }

    [Logged(AspectType.OnEntry)]
    public int Sum(int a, int b) => a + b;
}

public sealed class ChdExceptionSum
{
    public Microsoft.AspNetCore.Http.HttpContext? HttpContext { get; set; }

    [Logged(AspectType.OnException)]
    public int Fail(int a, int b) => throw new InvalidOperationException("sum failed");
}

public sealed class SerilogFileInterceptor : IInterceptor
{
    private readonly bool _onException;

    public SerilogFileInterceptor(bool onException = false) => _onException = onException;
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        Formatting = Formatting.None
    };

    private static readonly MessageTemplate EntryTemplate = new MessageTemplateParser().Parse(
        "The {ClassName}.{MethodName} method started working with \"{ParameterValues}\" parameters.");

    private static readonly MessageTemplate ErrorTemplate = new MessageTemplateParser().Parse(
        "The {ClassName}.{MethodName} method failed(\"{Message}\") with the input (\"{ParameterValues}\"). Stack trace: \"{ReturnValueJson}\"");

    private static readonly string? SourceContextName = Assembly.GetEntryAssembly()?.GetName().Name;

    public static ILogger Logger { get; set; } = Serilog.Core.Logger.None;

    public void Intercept(IInvocation invocation)
    {
        if (_onException)
        {
            try
            {
                invocation.Proceed();
            }
            catch (Exception ex)
            {
                Write(invocation, LogEventLevel.Error, "Error", "OnException", ex.GetAllMessages(), ex.StackTrace);
                throw;
            }

            return;
        }

        Write(invocation, LogEventLevel.Information, "Information", "OnEntry", null, null);
        invocation.Proceed();
    }

    private static void Write(
        IInvocation invocation,
        LogEventLevel level,
        string messageType,
        string aspectEvent,
        string? message,
        string? returnValue)
    {
        var target = invocation.InvocationTarget ?? invocation.Proxy;
        _ = target?.GetType().GetProperty("HttpContext")?.GetValue(target);
        var type = target?.GetType();
        var parameters = JsonConvert.SerializeObject(invocation.Arguments, JsonSettings);
        var template = level == LogEventLevel.Error ? ErrorTemplate : EntryTemplate;
        var properties = new LogEventProperty[19];
        properties[0] = new LogEventProperty("SourceContext", new ScalarValue(SourceContextName ?? ""));
        properties[1] = new LogEventProperty("ClassName", new ScalarValue(type?.Name));
        properties[2] = new LogEventProperty("MethodName", new ScalarValue(invocation.Method.Name));
        properties[3] = new LogEventProperty("ParameterValues", new ScalarValue(parameters));
        properties[4] = new LogEventProperty("MessageType", new ScalarValue(messageType));
        properties[5] = new LogEventProperty("AspectEvent", new ScalarValue(aspectEvent));
        properties[6] = new LogEventProperty("AssamblyName", new ScalarValue(type?.Assembly.FullName));
        properties[7] = new LogEventProperty("LoggingTime", new ScalarValue(DateTime.Now));
        properties[8] = new LogEventProperty("ReturnValueJson", new ScalarValue(returnValue));
        properties[9] = new LogEventProperty("ExecutionTimeMs", new ScalarValue(0L));
        properties[10] = new LogEventProperty("Message", new ScalarValue(message));
        properties[11] = new LogEventProperty("MessageNo", new ScalarValue(0));
        properties[12] = new LogEventProperty("UserInfo", new ScalarValue(null));
        properties[13] = new LogEventProperty("SessionInfo", new ScalarValue(null));
        properties[14] = new LogEventProperty("Host", new ScalarValue(null));
        properties[15] = new LogEventProperty("StatusCode", new ScalarValue(null));
        properties[16] = new LogEventProperty("ClientIP", new ScalarValue(null));
        properties[17] = new LogEventProperty("RequestPath", new ScalarValue(null));
        properties[18] = new LogEventProperty("ClaimsInfo", new ScalarValue(null));
        Logger.Write(new LogEvent(DateTimeOffset.Now, level, null, template, properties));
    }
}

public static class LoggingComparisonTest
{
    private const int BatchSize = 100;

    public static void Run()
    {
        Console.WriteLine("\n[Running] Logging aspect comparison (CHD vs Castle)...\n");
        Console.WriteLine("   Same method. Entry and exception. Same Serilog fields, separate files.");
        Console.WriteLine("   • CHD: [Logged(AspectType.OnEntry)] and [Logged(AspectType.OnException)]");
        Console.WriteLine("   • Castle: DynamicProxy interceptor");
        Console.WriteLine("   Grafana: http://localhost:3000/d/chd-logging");
        Console.WriteLine();

        EnsureWoven();
        var (chdLog, castleLog) = ConfigureFileLogger();
        Console.WriteLine($"   CHD file:    {chdLog}");
        Console.WriteLine($"   Castle file: {castleLog}");
        Console.WriteLine();

        var chd = new ChdLoggedSum();
        var chdException = new ChdExceptionSum();
        var generator = new ProxyGenerator();
        var castle = generator.CreateInterfaceProxyWithTarget<ILoggedSum>(
            new PlainSum(),
            new SerilogFileInterceptor());
        var castleException = generator.CreateInterfaceProxyWithTarget<ILoggedFailure>(
            new PlainFailure(),
            new SerilogFileInterceptor(onException: true));

        chd.Sum(1, 2);
        castle.Sum(1, 2);
        SwallowFailure(() => chdException.Fail(1, 2));
        SwallowFailure(() => castleException.Fail(1, 2));

        var scenario = Scenario.Create("logging_comparison", async context =>
        {
            var failed = false;
            var value = context.ScenarioInfo.InstanceNumber;

            if (!Measure("chd", "entry", () => chd.Sum(value, 2)))
                failed = true;

            if (!Measure("castle", "entry", () => castle.Sum(value, 2)))
                failed = true;

            if (!Measure("chd", "exception", () => SwallowFailure(() => chdException.Fail(value, 2))))
                failed = true;

            if (!Measure("castle", "exception", () => SwallowFailure(() => castleException.Fail(value, 2))))
                failed = true;

            return failed ? Response.Fail() : Response.Ok();
        })
        .WithoutWarmUp()
        .WithLoadSimulations(
            Simulation.KeepConstant(copies: 1, during: TimeSpan.FromSeconds(20)),
            Simulation.KeepConstant(copies: 2, during: TimeSpan.FromSeconds(20)),
            Simulation.KeepConstant(copies: 4, during: TimeSpan.FromSeconds(20))
        );

        NBomberRunner
            .RegisterScenarios(scenario)
            .Run();

        Console.WriteLine("\n[Completed] Logging aspect comparison finished.");
        Console.WriteLine("   Dashboard: http://localhost:3000/d/chd-logging\n");
    }

    private static void EnsureWoven()
    {
        var il = typeof(ChdLoggedSum).GetMethod(nameof(ChdLoggedSum.Sum))!.GetMethodBody()?.GetILAsByteArray();
        if (il == null || il.Length < 80)
            throw new InvalidOperationException("[Logged] was not woven. MethodBoundaryAspect.Fody did not run.");
    }

    private static (string ChdLog, string CastleLog) ConfigureFileLogger()
    {
        var logDir = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(logDir);

        var initPath = Path.Combine(logDir, "chd-init.log");
        var chdLog = Path.Combine(logDir, "aspect-chd.log");
        var castleLog = Path.Combine(logDir, "aspect-castle.log");
        File.WriteAllText(
            Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
            $$"""
            {
              "LogConfig": {
                "Server": "127.0.0.1",
                "Port": "12201",
                "Path": "{{initPath.Replace("\\", "\\\\")}}",
                "PathFormat": "{{Path.Combine(logDir, "chd-init-{Date}.log").Replace("\\", "\\\\")}}",
                "ApplicationName": "Chd.LoadTests",
                "FileSizeLimitBytes": 1048576,
                "RetainedFileCountLimit": 1,
                "IsLogSessionInfo": false
              }
            }
            """);

        LoggingDependencyInjectionExtensions.UseLogger(null);

        var previous = Log.Logger;
        Log.Logger = FileLogger(chdLog);
        SerilogFileInterceptor.Logger = FileLogger(castleLog);
        (previous as IDisposable)?.Dispose();
        return (chdLog, castleLog);
    }

    private static ILogger FileLogger(string path) => new LoggerConfiguration()
        .MinimumLevel.Information()
        .WriteTo.File(
            path,
            fileSizeLimitBytes: 52_428_800,
            rollOnFileSizeLimit: true,
            retainedFileCountLimit: 2)
        .CreateLogger();

    private static void SwallowFailure(Action fail)
    {
        try
        {
            fail();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static bool Measure(string library, string aspectEvent, Action call)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            for (var i = 0; i < BatchSize; i++)
                call();

            stopwatch.Stop();
            var elapsed = stopwatch.Elapsed.TotalSeconds;
            PrometheusMetrics.LoggingCalls.WithLabels(library, aspectEvent, "success").Inc(BatchSize);
            PrometheusMetrics.LoggingCallLatency.WithLabels(library, aspectEvent).Observe(elapsed / BatchSize);
            PrometheusMetrics.LoggingBatchLatency.WithLabels(library, aspectEvent).Observe(elapsed);
            return true;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            PrometheusMetrics.LoggingCalls.WithLabels(library, aspectEvent, "failed").Inc();
            Console.WriteLine($"Logging failed ({library}): {ex.Message}");
            return false;
        }
    }
}
