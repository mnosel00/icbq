using ExTCCM.Database.Context;
using ExTCCM.Services;
using Microsoft.AspNetCore.Components.WebView.Wpf;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using QuestPDF.Infrastructure;
using System;
using System.IO;
using System.Windows;

namespace ExTCCM
{
    public partial class MainWindow : Window
    {
        public IServiceProvider Services { get; }

        public MainWindow()
        {
            InitializeComponent();

            // Retain existing QuestPDF license configuration
            QuestPDF.Settings.License = LicenseType.Community;

            var serviceCollection = new ServiceCollection();

            // 1. Blazor & MudBlazor Services
            serviceCollection.AddWpfBlazorWebView();
            serviceCollection.AddMudServices();

            // 2. Configuration
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            serviceCollection.AddSingleton<IConfiguration>(configuration);

            // 3. Register Backend Logic
            serviceCollection.AddSingleton<StatsService>();
            serviceCollection.AddTransient<StatsDbContext>();

            // 4. Build and assign to Resources for BlazorWebView
            Services = serviceCollection.BuildServiceProvider();
            Resources.Add("services", Services);

            // 5. Programmatically register the Root Component to avoid XAML type resolution issues
            MainBlazorWebView.RootComponents.Add(new RootComponent
            {
                Selector = "#app",
                ComponentType = typeof(Routes)
            });
        }
    }
}