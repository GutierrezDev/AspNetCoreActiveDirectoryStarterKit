﻿using System;
using System.Net;
using System.Security.Claims;
using Asp.Core;
using Asp.Data;
using Asp.Emails;
using Asp.NovellDirectoryLdap;
using Asp.Repositories.Authentication;
using Asp.Repositories.Domains;
using Asp.Repositories.Logging;
using Asp.Repositories.Messages;
using Asp.Repositories.Roles;
using Asp.Repositories.Settings;
using Asp.Repositories.Users;
using Asp.Web.Common;
using Asp.Web.Common.Mvc;
using Asp.Web.Common.Security;
using AutoMapper;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.Mvc.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Serialization;
using NLog;
using NLog.Extensions.Logging;
using NLog.Web;

namespace Asp.Web
{
    public class Startup
    {
        public Startup(IHostingEnvironment env)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true)
                .AddEnvironmentVariables();
            Configuration = builder.Build();

            env.ConfigureNLog("nlog.config");
        }

        public IConfigurationRoot Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddOptions();
            services.Configure<AppSettings>(Configuration.GetSection("AppSettings"));

            services.AddDbContext<AppDbContext>(options =>
                options.UseSqlServer(Configuration.GetConnectionString("DefaultConnection"),
                    // TODO: Remove this if you use SQL 2012 or higher.
                    b => b.UseRowNumberForPaging()));

            services.AddAutoMapper();
            services.AddMemoryCache();
            services.AddSession();

            // Add AuthorizeFilter to demand the user to be authenticated in order to access resources.
            services.AddMvc(options => options.Filters.Add(new AuthorizeFilter(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())))
                // Maintain property names during serialization. See: https://github.com/aspnet/Announcements/issues/194
                .AddJsonOptions(options => options.SerializerSettings.ContractResolver = new DefaultContractResolver());

            // Set up policies from claims
            // https://leastprivilege.com/2016/08/21/why-does-my-authorize-attribute-not-work/
            services.AddAuthorization(options =>
            {
                options.AddPolicy(Constants.RoleNames.Administrator, policyBuilder =>
                {
                    policyBuilder.RequireAuthenticatedUser()
                        .RequireAssertion(context => context.User.HasClaim(ClaimTypes.Role, Constants.RoleNames.Administrator))
                        .Build();
                });
            });

            services.AddKendo();

            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            services.AddScoped<IAuthenticationService, LdapAuthenticationService>();
            services.AddScoped<IDbContext, AppDbContext>();
            services.AddScoped<IDomainRepository, DomainRepository>();
            services.AddScoped<IEmailSender, EmailSender>();
            services.AddScoped<IEmailTemplateRepository, EmailTemplateRepository>();
            services.AddScoped<ILogRepository, LogRepository>();
            services.AddScoped<IMessageService, MessageService>();
            services.AddScoped<LogFilter>();
            services.AddScoped<IRoleRepository, RoleRepository>();
            services.AddScoped<ISettingRepository, SettingRepository>();
            services.AddScoped<ISignInManager, SignInManager>();
            services.AddScoped<IUserSession, UserSession>();
            services.AddScoped<IUserRepository, UserRepository>();
            services.AddTransient<IDateTime, DateTimeAdapter>();
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseStatusCodePagesWithRedirects("/Common/Error/{0}");
                app.UseExceptionHandler("/Common/Error");
            }

            loggerFactory.AddNLog();
            app.AddNLogWeb();
            LogManager.Configuration.Variables["connectionString"] = Configuration.GetConnectionString("DefaultConnection");
            LogManager.Configuration.Variables["configDir"] = @"C:\Projects\AspNetCoreActiveDirectoryStarterKit";


            // https://docs.microsoft.com/en-us/aspnet/core/security/authentication/cookie
            // https://github.com/leastprivilege/AspNetCoreSecuritySamples/blob/master/Authorization/src/AspNetCoreAuthentication/Startup.cs#L76
            app.UseCookieAuthentication(new CookieAuthenticationOptions
            {
                Events = new CookieAuthenticationEvents
                {
                    // You will need this only if you use Ajax calls with a library not compatible with IsAjaxRequest
                    // More info here: https://github.com/aspnet/Security/issues/1056
                    OnRedirectToAccessDenied = context =>
                    {
                        context.Response.StatusCode = (int) HttpStatusCode.Forbidden;
                        return TaskCache.CompletedTask;
                    }
                },
                ExpireTimeSpan = TimeSpan.FromMinutes(Int32.Parse(Configuration.GetSection("AppSettings:CookieAuthentication:ExpireMinutes").Value)),
                AuthenticationScheme = Constants.AuthenticationScheme,
                LoginPath = new PathString("/Account/Login"),
                AccessDeniedPath = new PathString("/Common/AccessDenied"),
                AutomaticAuthenticate = true,
                AutomaticChallenge = true
            });

            app.UseStaticFiles();
            app.UseSession();
            app.UseMvc(routes =>
            {
                routes.MapRoute("areaRoute", "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}");
                routes.MapRoute("default", "{controller=Home}/{action=Index}/{id?}");
            });

            app.UseKendo(env);
        }
    }
}