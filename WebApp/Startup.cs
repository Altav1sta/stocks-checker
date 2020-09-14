using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;

namespace WebApp
{
    internal sealed class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllersWithViews();
            services.Configure<Credentials>(Configuration);
            services.AddDbContextPool<ApplicationDbContext>(x => x.UseMySql(Configuration.GetConnectionString("MySql")));
            services.AddSingleton<Bot>();
            services.AddSingleton<TinkoffClient>();
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILogger<Startup> logger)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }
            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseRouting();
            app.UseAuthorization();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}");
            });

            try
            {
                using var scope = app.ApplicationServices.GetRequiredService<IServiceScopeFactory>().CreateScope();
                using var context = scope.ServiceProvider.GetService<ApplicationDbContext>();

                context.Database.Migrate();

                var client = app.ApplicationServices.GetService<TinkoffClient>();
                client.SubscribeForAllCandles().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in Configure");
            }
        }
    }
}
