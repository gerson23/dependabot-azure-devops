﻿using Medallion.Threading;
using Medallion.Threading.FileSystem;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.FeatureManagement;
using Tingle.Dependabot.ApplicationInsights;
using Tingle.Dependabot.FeatureManagement;
using Tingle.Dependabot.Workflow;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Extensions on <see cref="IServiceCollection"/>.</summary>
public static class IServiceCollectionExtensions
{
    /// <summary>Add standard Application Insights services.</summary>
    /// <param name="services">The <see cref="IServiceCollection"/> instance to add to.</param>
    /// <param name="configuration">The root configuration instance from which to pull settings.</param>
    public static IServiceCollection AddStandardApplicationInsights(this IServiceCollection services, IConfiguration configuration)
    {
        // Add the core services
        services.AddApplicationInsightsTelemetry(configuration);

        // Add background service to flush telemetry on shutdown
        services.AddHostedService<InsightsShutdownFlushService>();

        // Add processors
        services.AddApplicationInsightsTelemetryProcessor<InsightsFilteringProcessor>();

        // Enrich the telemetry with various sources of information
        services.AddHttpContextAccessor(); // Required to resolve the request from the HttpContext
                                           // according to docs link below, this registration should be singleton
                                           // https://docs.microsoft.com/en-us/azure/azure-monitor/app/asp-net-core#adding-telemetryinitializers
        services.AddSingleton<ITelemetryInitializer, DependabotTelemetryInitializer>();
        // services.AddApplicationInsightsTelemetryExtras(); // Add other extras

        // services.AddActivitySourceDependencyCollector(new[] {
        //     "Tingle.EventBus",
        // });

        return services;
    }

    public static IServiceCollection AddDistributedLockProvider(this IServiceCollection services, IHostEnvironment environment, IConfiguration configuration)
    {
        var configKey = ConfigurationPath.Combine("DistributedLocking", "FilePath");

        var path = configuration.GetValue<string?>(configKey);

        // when the path is null in development, set one
        if (string.IsNullOrWhiteSpace(path) && environment.IsDevelopment())
        {
            path = Path.Combine(environment.ContentRootPath, "distributed-locks");
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException($"'{nameof(path)}' must be provided via configuration at '{configKey}'.");
        }

        services.AddSingleton<IDistributedLockProvider>(new FileDistributedSynchronizationProvider(new(path)));

        return services;
    }

    public static IServiceCollection AddStandardFeatureManagement(this IServiceCollection services)
    {
        var builder = services.AddFeatureManagement();

        builder.AddFeatureFilter<FeatureManagement.FeatureFilters.PercentageFilter>();
        builder.AddFeatureFilter<FeatureManagement.FeatureFilters.TimeWindowFilter>();
        builder.AddFeatureFilter<FeatureManagement.FeatureFilters.ContextualTargetingFilter>();

        builder.Services.AddSingleton<FeatureManagement.FeatureFilters.ITargetingContextAccessor, ProjectTargetingContextAccessor>();
        builder.AddFeatureFilter<FeatureManagement.FeatureFilters.TargetingFilter>(); // requires ITargetingContextAccessor
        builder.Services.Configure<FeatureManagement.FeatureFilters.TargetingEvaluationOptions>(o => o.IgnoreCase = true);

        builder.UseDisabledFeaturesHandler(new Tingle.Dependabot.CustomDisabledFeaturesHandler());

        return services;
    }

    public static IServiceCollection AddWorkflowServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<WorkflowOptions>(configuration);
        services.ConfigureOptions<WorkflowConfigureOptions>();

        services.AddScoped<UpdateRunner>();
        services.AddSingleton<UpdateScheduler>();

        services.AddHttpClient<AzureDevOpsProvider>();
        services.AddScoped<Synchronizer>();

        return services;
    }
}
