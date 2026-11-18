using AutoMapper;
using Chd.Mapping.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NBomber.CSharp;

namespace Chd.LoadTests;

[MapTo(typeof(OrderEntity))]
public partial class OrderDto
{
    public decimal Price { get; set; }
    public decimal Tax { get; set; }
    public decimal Discount { get; set; }

    [MapProperty("Price * (Tax + 100) / 100 - Discount")]
    public decimal NetTotal { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Surname { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    [MapProperty("IsActive ? 'Active' : 'Passive'")]
    public string StatusText { get; set; } = string.Empty;
}

public partial class OrderEntity
{
    public decimal Price { get; set; }
    public decimal Tax { get; set; }
    public decimal Discount { get; set; }
    public decimal NetTotal { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Surname { get; set; } = string.Empty;
    public string StatusText { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public static class MappingComparisonTest
{
    private const int BatchSize = 1000;
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(60);

    public static void Run()
    {
        Console.WriteLine("\n[Running] Mapping comparison (CHD vs AutoMapper)...\n");
        Console.WriteLine($"   Batch size: {BatchSize} items");
        Console.WriteLine($"   Duration:   {Duration.TotalSeconds:0} seconds");
        Console.WriteLine("   Grafana:    http://localhost:3000/d/chd-mapping");
        Console.WriteLine();

        var orders = CreateOrders();
        var mapper = CreateMapper();

        // Warm both mappers so the first measured batch is not a cold start.
        _ = (OrderEntity)orders[0];
        _ = mapper.Map<OrderEntity>(orders[0]);

        var scenario = Scenario.Create("mapping_comparison", async context =>
        {
            var failed = false;

            if (!Measure("chd", () => MapWithChd(orders)))
                failed = true;

            if (!Measure("automapper", () => MapWithAutoMapper(mapper, orders)))
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

        Console.WriteLine("\n[Completed] Mapping comparison finished.");
        Console.WriteLine("   Dashboard: http://localhost:3000/d/chd-mapping\n");
    }

    private static bool Measure(string mapper, Action map)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            map();
            stopwatch.Stop();

            var elapsed = stopwatch.Elapsed.TotalSeconds;
            PrometheusMetrics.MappingItems.WithLabels(mapper, "success").Inc(BatchSize);
            PrometheusMetrics.MappingItemLatency.WithLabels(mapper).Observe(elapsed / BatchSize);
            PrometheusMetrics.MappingBatchLatency.WithLabels(mapper).Observe(elapsed);
            return true;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            PrometheusMetrics.MappingItems.WithLabels(mapper, "failed").Inc();
            Console.WriteLine($"Mapping failed ({mapper}): {ex.Message}");
            return false;
        }
    }

    private static void MapWithChd(OrderDto[] orders)
    {
        OrderEntity entity = null!;
        for (var i = 0; i < orders.Length; i++)
            entity = orders[i];

        GC.KeepAlive(entity);
    }

    private static void MapWithAutoMapper(IMapper mapper, OrderDto[] orders)
    {
        OrderEntity entity = null!;
        for (var i = 0; i < orders.Length; i++)
            entity = mapper.Map<OrderEntity>(orders[i]);

        GC.KeepAlive(entity);
    }

    private static OrderDto[] CreateOrders()
    {
        var orders = new OrderDto[BatchSize];
        for (var i = 0; i < orders.Length; i++)
        {
            orders[i] = new OrderDto
            {
                Price = 100 + i,
                Tax = 18,
                Discount = 2,
                IsActive = i % 2 == 0,
                Name = "Mehmet",
                Surname = "Yoldas"
            };
        }

        return orders;
    }

    private static IMapper CreateMapper()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAutoMapper(cfg => cfg.CreateMap<OrderDto, OrderEntity>()
            .ForMember(d => d.NetTotal,
                o => o.MapFrom(s => s.Price * (s.Tax + 100) / 100 - s.Discount))
            .ForMember(d => d.StatusText,
                o => o.MapFrom(s => s.IsActive ? "Active" : "Passive")));

        return services.BuildServiceProvider().GetRequiredService<IMapper>();
    }
}
