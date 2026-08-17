using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using MeridianStudio.API.Application.Contracts;

namespace MeridianStudio.API.Application.Services;

/// <summary>
/// Generates a complete, runnable project scaffold for the requested language and packages it
/// as an in-memory zip archive. Project config / boilerplate files come from hardcoded templates;
/// the integration-step code bodies (from the LLM or heuristic engine) are embedded in the
/// appropriate service layer with corrected namespace declarations.
/// </summary>
public static class ProjectGeneratorService
{
    public static string GetProjectName(string raw) => SafeName(raw);

    public static byte[] GenerateZip(GenerateProjectRequest req)
    {
        var name  = SafeName(req.SolutionName);
        var lang  = (req.Language ?? "csharp").ToLowerInvariant();
        var steps = req.IntegrationSteps ?? [];
        var codes = req.StepCodes        ?? [];
        var desc  = req.Description?.Trim() ?? req.SolutionName;

        var files = lang switch
        {
            "typescript" => TypeScriptProject(name, desc, req, steps, codes),
            "python"     => PythonProject(name, desc, req, steps, codes),
            "java"       => JavaProject(name, desc, req, steps, codes),
            "go"         => GoProject(name, desc, req, steps, codes),
            _            => CSharpProject(name, desc, req, steps, codes),
        };

        return Zip(files, name);
    }

    // ── C# / .NET 10 ─────────────────────────────────────────────────────────

    private static List<(string Path, string Content)> CSharpProject(
        string n, string desc, GenerateProjectRequest req, string[] steps, string[] codes)
    {
        var (ag, dg, apg, ig, tg) = (NewG(), NewG(), NewG(), NewG(), NewG());
        var sg = NewG();
        var stepFiles = BuildStepFiles(steps, codes, n, "csharp");
        var readmeRun = $"dotnet build {n}.sln\n  dotnet run --project src/{n}.API";

        var files = new List<(string, string)>
        {
            ($"{n}.sln",                                          CsSln(n, ag, dg, apg, ig, tg, sg)),
            ($"src/{n}.API/{n}.API.csproj",                      CsApiCsproj(n)),
            ($"src/{n}.API/Program.cs",                          CsProgram(n)),
            ($"src/{n}.API/appsettings.json",                    CsAppsettings(n)),
            ($"src/{n}.API/appsettings.Development.json",        CsAppsettingsDev()),
            ($"src/{n}.Domain/{n}.Domain.csproj",                CsDomainCsproj(n)),
            ($"src/{n}.Domain/Models/{n}Entity.cs",              CsDomainModel(n, desc)),
            ($"src/{n}.Application/{n}.Application.csproj",      CsAppCsproj(n)),
            ($"src/{n}.Application/Interfaces/I{n}Service.cs",   CsInterface(n, desc)),
            ($"src/{n}.Infrastructure/{n}.Infrastructure.csproj", CsInfraCsproj(n)),
            ($"src/{n}.Infrastructure/Data/{n}DbContext.cs",     CsDbContext(n)),
            ($"src/{n}.Infrastructure/Data/Migrations/001_InitialCreate.sql", CsMigration(n)),
            ($"tests/{n}.Tests/{n}.Tests.csproj",                CsTestCsproj(n)),
            ($"tests/{n}.Tests/Services/{n}ServiceTests.cs",     CsTests(n)),
            (".gitignore",                                        DotnetGitignore()),
            ("docker-compose.yml",                               DockerCompose(n)),
            ("README.md",                                        Readme(n, desc, req, steps, "C# .NET 10",
                                                                    readmeRun, [".NET 10 SDK", "PostgreSQL 16 (or run via Docker Compose)"])),
        };

        foreach (var (fileName, content) in stepFiles)
            files.Insert(9, ($"src/{n}.Application/Services/{fileName}.cs", content));

        return files;
    }

    // ── TypeScript / Node.js ─────────────────────────────────────────────────

    private static List<(string Path, string Content)> TypeScriptProject(
        string n, string desc, GenerateProjectRequest req, string[] steps, string[] codes)
    {
        var stepFiles = BuildStepFiles(steps, codes, n, "typescript");
        var readmeRun = "npm install\n  npm run dev";

        var files = new List<(string, string)>
        {
            ("package.json",          TsPackage(n, desc)),
            ("tsconfig.json",         TsConfig()),
            (".env.example",          TsEnvExample(n)),
            ("src/index.ts",          TsIndex(n)),
            ("src/app.ts",            TsApp(n)),
            ($"src/models/{Camel(n)}.model.ts",      TsModel(n, desc)),
            ($"src/config/database.ts",              TsDatabase(n)),
            ($"src/middleware/errorHandler.ts",       TsErrorHandler()),
            (".gitignore",            NodeGitignore()),
            ("README.md",             Readme(n, desc, req, steps, "TypeScript / Node.js",
                                         readmeRun, ["Node.js 20+", "PostgreSQL 16 (optional)"])),
        };

        foreach (var (fileName, content) in stepFiles)
            files.Insert(5, ($"src/services/{fileName}.ts", content));

        return files;
    }

    // ── Python ───────────────────────────────────────────────────────────────

    private static List<(string Path, string Content)> PythonProject(
        string n, string desc, GenerateProjectRequest req, string[] steps, string[] codes)
    {
        var snake = Snake(n);
        var stepFiles = BuildStepFiles(steps, codes, n, "python");
        var readmeRun = "pip install -r requirements.txt\n  uvicorn main:app --reload";

        var files = new List<(string, string)>
        {
            ("requirements.txt",           PyRequirements()),
            ("pyproject.toml",             PyProject(n, desc)),
            (".env.example",               PyEnvExample(n)),
            ("main.py",                    PyMain(n, snake)),
            ($"src/{snake}/models.py",     PyModel(n, desc)),
            ($"src/{snake}/database.py",   PyDatabase(n)),
            ($"src/{snake}/router.py",     PyRouter(n, snake)),
            ("alembic.ini",                PyAlembicIni()),
            (".gitignore",                 PythonGitignore()),
            ("README.md",                  Readme(n, desc, req, steps, "Python / FastAPI",
                                               readmeRun, ["Python 3.12+", "PostgreSQL 16 (optional)"])),
        };

        foreach (var (fileName, content) in stepFiles)
            files.Insert(4, ($"src/{snake}/{fileName}.py", content));

        return files;
    }

    // ── Java / Spring Boot ───────────────────────────────────────────────────

    private static List<(string Path, string Content)> JavaProject(
        string n, string desc, GenerateProjectRequest req, string[] steps, string[] codes)
    {
        var pkg = $"com.meridian.{n.ToLowerInvariant()}";
        var dir = $"src/main/java/{pkg.Replace('.', '/')}";
        var stepFiles = BuildStepFiles(steps, codes, n, "java");
        var readmeRun = "mvn spring-boot:run";

        var files = new List<(string, string)>
        {
            ("pom.xml",                              JavaPom(n, desc, pkg)),
            ($"{dir}/{n}Application.java",           JavaApp(n, pkg)),
            ($"{dir}/model/{n}.java",                JavaModel(n, desc, pkg)),
            ($"{dir}/repository/{n}Repository.java", JavaRepo(n, pkg)),
            ($"{dir}/controller/{n}Controller.java", JavaController(n, pkg)),
            ("src/main/resources/application.yml",   JavaAppYml(n)),
            (".gitignore",                            JavaGitignore()),
            ("README.md",                             Readme(n, desc, req, steps, "Java / Spring Boot",
                                                         readmeRun, ["Java 21+", "Maven 3.9+", "PostgreSQL 16 (optional)"])),
        };

        foreach (var (fileName, content) in stepFiles)
            files.Insert(4, ($"{dir}/service/{fileName}.java", content));

        return files;
    }

    // ── Go ───────────────────────────────────────────────────────────────────

    private static List<(string Path, string Content)> GoProject(
        string n, string desc, GenerateProjectRequest req, string[] steps, string[] codes)
    {
        var pkg = n.ToLowerInvariant();
        var stepFiles = BuildStepFiles(steps, codes, n, "go");
        var readmeRun = "go run ./cmd/server";

        var files = new List<(string, string)>
        {
            ("go.mod",                           GoMod(pkg)),
            ("cmd/server/main.go",               GoMain(pkg, n)),
            ($"internal/{pkg}/model.go",         GoModel(pkg, n, desc)),
            ($"internal/{pkg}/repository.go",    GoRepo(pkg, n)),
            ($"internal/{pkg}/handler.go",       GoHandler(pkg, n)),
            ("internal/database/connection.go",  GoDatabase(pkg)),
            ("config/config.go",                 GoConfig(pkg, n)),
            (".env.example",                     GoEnvExample(n)),
            (".gitignore",                        GoGitignore()),
            ("README.md",                         Readme(n, desc, req, steps, "Go",
                                                     readmeRun, ["Go 1.22+", "PostgreSQL 16 (optional)"])),
        };

        foreach (var (fileName, content) in stepFiles)
            files.Insert(4, ($"internal/{pkg}/{fileName}.go", content));

        return files;
    }

    // ── Step file builder ─────────────────────────────────────────────────────

    private record StepFile(string FileName, string Content);

    private static StepFile[] BuildStepFiles(
        string[] steps, string[] codes, string projectName, string lang)
    {
        var result = new List<StepFile>();
        for (var i = 0; i < steps.Length; i++)
        {
            var stepLabel = steps[i];
            var raw       = i < codes.Length ? codes[i] : string.Empty;
            var fileName  = StepFileName(stepLabel, i + 1, lang);
            var content   = FixStepCode(raw, stepLabel, projectName, lang, i + 1);
            result.Add(new StepFile(fileName, content));
        }
        return [.. result];
    }

    private static string StepFileName(string stepLabel, int idx, string lang)
    {
        var words = Regex.Replace(stepLabel, @"[^\w\s]", "")
                         .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                         .Take(4)
                         .Select(w => char.ToUpper(w[0]) + w[1..]);
        return lang switch
        {
            "python" or "go" => $"step{idx:00}_{string.Concat(words).ToLowerInvariant()}",
            _                => $"Step{idx:00}_{string.Concat(words)}Service",
        };
    }

    private static string FixStepCode(
        string raw, string stepLabel, string projectName, string lang, int stepIdx)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return lang switch
            {
                "typescript" => $$"""
                    // Step {{stepIdx}}: {{stepLabel}}
                    // TODO: implement this step

                    export class Step{{stepIdx:00}}Service {
                      async execute(): Promise<void> {
                        throw new Error('Not implemented');
                      }
                    }
                    """,
                "python" => $$"""
                    # Step {{stepIdx}}: {{stepLabel}}
                    # TODO: implement this step

                    async def execute() -> None:
                        raise NotImplementedError
                    """,
                "java" => $$"""
                    package com.meridian.{{projectName.ToLowerInvariant()}}.service;

                    import org.springframework.stereotype.Service;

                    // Step {{stepIdx}}: {{stepLabel}}
                    @Service
                    public class Step{{stepIdx:00}}Service {
                        public void execute() {
                            throw new UnsupportedOperationException("Not implemented");
                        }
                    }
                    """,
                "go" => $$"""
                    package {{projectName.ToLowerInvariant()}}

                    import "errors"

                    // Step{{stepIdx:00}}: {{stepLabel}}
                    func Execute{{stepIdx:00}}() error {
                        return errors.New("not implemented")
                    }
                    """,
                _ => $$"""
                    namespace {{projectName}}.Application.Services;

                    // Step {{stepIdx}}: {{stepLabel}}
                    // TODO: implement this step
                    public sealed class Step{{stepIdx:00}}Service
                    {
                        public Task ExecuteAsync(CancellationToken ct = default)
                            => throw new NotImplementedException();
                    }
                    """,
            };
        }

        return lang switch
        {
            "csharp" => Regex.Replace(raw,
                @"(?m)^namespace\s+\S+;",
                $"namespace {projectName}.Application.Services;"),
            "typescript" => raw,
            "python"     => raw,
            "java" => Regex.Replace(raw,
                @"(?m)^package\s+\S+;",
                $"package com.meridian.{projectName.ToLowerInvariant()}.service;"),
            "go" => Regex.Replace(raw,
                @"(?m)^package\s+\w+",
                $"package {projectName.ToLowerInvariant()}"),
            _ => raw,
        };
    }

    // ── C# templates ─────────────────────────────────────────────────────────

    private static string CsSln(
        string n, string ag, string dg, string apg, string ig, string tg, string sg) => $$"""
        Microsoft Visual Studio Solution File, Format Version 12.00
        # Visual Studio Version 17
        VisualStudioVersion = 17.11.35222.181
        MinimumVisualStudioVersion = 10.0.40219.1
        Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "{{n}}.API", "src\{{n}}.API\{{n}}.API.csproj", "{{ag}}"
        EndProject
        Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "{{n}}.Domain", "src\{{n}}.Domain\{{n}}.Domain.csproj", "{{dg}}"
        EndProject
        Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "{{n}}.Application", "src\{{n}}.Application\{{n}}.Application.csproj", "{{apg}}"
        EndProject
        Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "{{n}}.Infrastructure", "src\{{n}}.Infrastructure\{{n}}.Infrastructure.csproj", "{{ig}}"
        EndProject
        Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "{{n}}.Tests", "tests\{{n}}.Tests\{{n}}.Tests.csproj", "{{tg}}"
        EndProject
        Global
            GlobalSection(SolutionConfigurationPlatforms) = preSolution
                Debug|Any CPU = Debug|Any CPU
                Release|Any CPU = Release|Any CPU
            EndGlobalSection
            GlobalSection(ProjectConfigurationPlatforms) = postSolution
                {{ag}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
                {{ag}}.Debug|Any CPU.Build.0 = Debug|Any CPU
                {{ag}}.Release|Any CPU.ActiveCfg = Release|Any CPU
                {{ag}}.Release|Any CPU.Build.0 = Release|Any CPU
                {{dg}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
                {{dg}}.Debug|Any CPU.Build.0 = Debug|Any CPU
                {{dg}}.Release|Any CPU.ActiveCfg = Release|Any CPU
                {{dg}}.Release|Any CPU.Build.0 = Release|Any CPU
                {{apg}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
                {{apg}}.Debug|Any CPU.Build.0 = Debug|Any CPU
                {{apg}}.Release|Any CPU.ActiveCfg = Release|Any CPU
                {{apg}}.Release|Any CPU.Build.0 = Release|Any CPU
                {{ig}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
                {{ig}}.Debug|Any CPU.Build.0 = Debug|Any CPU
                {{ig}}.Release|Any CPU.ActiveCfg = Release|Any CPU
                {{ig}}.Release|Any CPU.Build.0 = Release|Any CPU
                {{tg}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
                {{tg}}.Debug|Any CPU.Build.0 = Debug|Any CPU
                {{tg}}.Release|Any CPU.ActiveCfg = Release|Any CPU
                {{tg}}.Release|Any CPU.Build.0 = Release|Any CPU
            EndGlobalSection
            GlobalSection(SolutionProperties) = preSolution
                HideSolutionNode = FALSE
            EndGlobalSection
        EndGlobal
        """;

    private static string CsApiCsproj(string n) => $$"""
        <Project Sdk="Microsoft.NET.Sdk.Web">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
            <RootNamespace>{{n}}.API</RootNamespace>
            <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
          </PropertyGroup>
          <ItemGroup>
            <ProjectReference Include="..\{{n}}.Application\{{n}}.Application.csproj" />
            <ProjectReference Include="..\{{n}}.Infrastructure\{{n}}.Infrastructure.csproj" />
          </ItemGroup>
          <ItemGroup>
            <PackageReference Include="Scalar.AspNetCore" Version="1.*" />
            <PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="10.*" />
            <PackageReference Include="Serilog.AspNetCore" Version="8.*" />
            <PackageReference Include="Serilog.Sinks.Console" Version="5.*" />
          </ItemGroup>
        </Project>
        """;

    private static string CsProgram(string n) => $$"""
        using Serilog;
        using {{n}}.Application;
        using {{n}}.Infrastructure;

        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .CreateBootstrapLogger();

        var builder = WebApplication.CreateBuilder(args);
        builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration));

        builder.Services.AddOpenApi();
        builder.Services.AddApplication();
        builder.Services.AddInfrastructure(builder.Configuration);
        builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

        var app = builder.Build();

        app.UseHttpsRedirection();
        app.UseCors();
        app.UseSerilogRequestLogging();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.MapScalarApiReference();
        }

        app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "{{n}}.API", utc = DateTimeOffset.UtcNow }))
           .WithTags("Health");

        app.Run();
        """;

    private static string CsAppsettings(string n) => $$"""
        {
          "Serilog": {
            "MinimumLevel": { "Default": "Information", "Override": { "Microsoft": "Warning" } }
          },
          "ConnectionStrings": {
            "Default": "Host=localhost;Database={{n.ToLowerInvariant()}};Username=postgres;Password=postgres"
          },
          "AllowedHosts": "*"
        }
        """;

    private static string CsAppsettingsDev() =>
        """{"Logging":{"LogLevel":{"Default":"Debug","Microsoft.AspNetCore":"Warning"}}}""";

    private static string CsDomainCsproj(string n) => $$"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
            <RootNamespace>{{n}}.Domain</RootNamespace>
          </PropertyGroup>
        </Project>
        """;

    private static string CsDomainModel(string n, string desc) => $$"""
        namespace {{n}}.Domain.Models;

        /// <summary>{{desc}}</summary>
        public sealed record {{n}}Entity
        {
            public Guid   Id          { get; init; } = Guid.NewGuid();
            public string Name        { get; init; } = string.Empty;
            public string Description { get; init; } = string.Empty;
            public string Status      { get; init; } = "Active";
            public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
            public DateTimeOffset? UpdatedAt { get; init; }

            public static {{n}}Entity Create(string name, string description) =>
                new() { Name = name, Description = description };
        }
        """;

    private static string CsAppCsproj(string n) => $$"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
            <RootNamespace>{{n}}.Application</RootNamespace>
          </PropertyGroup>
          <ItemGroup>
            <ProjectReference Include="..\{{n}}.Domain\{{n}}.Domain.csproj" />
          </ItemGroup>
          <ItemGroup>
            <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.*" />
            <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.*" />
          </ItemGroup>
        </Project>
        """;

    private static string CsInterface(string n, string desc) => $$"""
        using {{n}}.Domain.Models;

        namespace {{n}}.Application.Interfaces;

        /// <summary>{{desc}}</summary>
        public interface I{{n}}Service
        {
            Task<IReadOnlyList<{{n}}Entity>> GetAllAsync(CancellationToken ct = default);
            Task<{{n}}Entity?>              GetByIdAsync(Guid id, CancellationToken ct = default);
            Task<{{n}}Entity>               CreateAsync(string name, string description, CancellationToken ct = default);
            Task                            DeleteAsync(Guid id, CancellationToken ct = default);
        }
        """;

    private static string CsInfraCsproj(string n) => $$"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
            <RootNamespace>{{n}}.Infrastructure</RootNamespace>
          </PropertyGroup>
          <ItemGroup>
            <ProjectReference Include="..\{{n}}.Application\{{n}}.Application.csproj" />
          </ItemGroup>
          <ItemGroup>
            <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.*">
              <PrivateAssets>all</PrivateAssets>
            </PackageReference>
            <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="9.*" />
          </ItemGroup>
        </Project>
        """;

    private static string CsDbContext(string n) => $$"""
        using Microsoft.EntityFrameworkCore;
        using {{n}}.Domain.Models;

        namespace {{n}}.Infrastructure.Data;

        public sealed class {{n}}DbContext(DbContextOptions<{{n}}DbContext> options)
            : DbContext(options)
        {
            public DbSet<{{n}}Entity> Items { get; set; } = null!;

            protected override void OnModelCreating(ModelBuilder mb)
            {
                mb.Entity<{{n}}Entity>(e =>
                {
                    e.HasKey(x => x.Id);
                    e.Property(x => x.Name).HasMaxLength(500).IsRequired();
                    e.Property(x => x.Description).HasMaxLength(4000);
                    e.Property(x => x.Status).HasMaxLength(50).HasDefaultValue("Active");
                    e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
                    e.HasIndex(x => x.Name);
                });
            }
        }
        """;

    private static string CsMigration(string n) => $$"""
        -- Migration: InitialCreate
        -- Project: {{n}}

        CREATE TABLE IF NOT EXISTS "Items" (
            "Id"          UUID            NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
            "Name"        VARCHAR(500)    NOT NULL,
            "Description" VARCHAR(4000)   NOT NULL DEFAULT '',
            "Status"      VARCHAR(50)     NOT NULL DEFAULT 'Active',
            "CreatedAt"   TIMESTAMPTZ     NOT NULL DEFAULT now(),
            "UpdatedAt"   TIMESTAMPTZ
        );

        CREATE INDEX idx_items_name ON "Items" ("Name");
        CREATE INDEX idx_items_status ON "Items" ("Status");

        -- Seed data
        INSERT INTO "Items" ("Name", "Description", "Status")
        VALUES ('Sample record', 'Inserted by InitialCreate migration', 'Active');
        """;

    private static string CsTestCsproj(string n) => $$"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
            <IsPackable>false</IsPackable>
          </PropertyGroup>
          <ItemGroup>
            <ProjectReference Include="..\..\src\{{n}}.Application\{{n}}.Application.csproj" />
          </ItemGroup>
          <ItemGroup>
            <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
            <PackageReference Include="xunit" Version="2.*" />
            <PackageReference Include="xunit.runner.visualstudio" Version="2.*">
              <PrivateAssets>all</PrivateAssets>
            </PackageReference>
            <PackageReference Include="Moq" Version="4.*" />
            <PackageReference Include="FluentAssertions" Version="6.*" />
          </ItemGroup>
        </Project>
        """;

    private static string CsTests(string n) => $$"""
        using FluentAssertions;
        using {{n}}.Domain.Models;

        namespace {{n}}.Tests.Services;

        public sealed class {{n}}ServiceTests
        {
            [Fact]
            public void Create_WithValidInputs_ReturnsEntity()
            {
                var entity = {{n}}Entity.Create("Test", "A test entity");
                entity.Name.Should().Be("Test");
                entity.Status.Should().Be("Active");
                entity.Id.Should().NotBeEmpty();
            }

            [Fact]
            public void Create_AssignsUtcTimestamp()
            {
                var before = DateTimeOffset.UtcNow;
                var entity = {{n}}Entity.Create("X", "Y");
                entity.CreatedAt.Should().BeOnOrAfter(before);
            }
        }
        """;

    // ── TypeScript templates ──────────────────────────────────────────────────

    private static string TsPackage(string n, string desc) => $$"""
        {
          "name": "{{n.ToLowerInvariant()}}",
          "version": "0.1.0",
          "description": "{{desc.Replace("\"", "\\\"")}}",
          "main": "dist/index.js",
          "scripts": {
            "dev": "ts-node-dev --respawn src/index.ts",
            "build": "tsc",
            "start": "node dist/index.js",
            "test": "jest"
          },
          "dependencies": {
            "express": "^4.19.0",
            "pg": "^8.12.0",
            "dotenv": "^16.4.0",
            "zod": "^3.23.0"
          },
          "devDependencies": {
            "@types/express": "^4.17.21",
            "@types/node": "^20.14.0",
            "@types/pg": "^8.11.6",
            "ts-node-dev": "^2.0.0",
            "typescript": "^5.5.0",
            "jest": "^29.7.0",
            "@types/jest": "^29.5.12",
            "ts-jest": "^29.2.0"
          }
        }
        """;

    private static string TsConfig() => """
        {
          "compilerOptions": {
            "target": "ES2022",
            "module": "commonjs",
            "lib": ["ES2022"],
            "outDir": "dist",
            "rootDir": "src",
            "strict": true,
            "esModuleInterop": true,
            "forceConsistentCasingInFileNames": true,
            "skipLibCheck": true,
            "resolveJsonModule": true
          },
          "include": ["src/**/*"],
          "exclude": ["node_modules", "dist"]
        }
        """;

    private static string TsEnvExample(string n) => $"""
        PORT=3000
        NODE_ENV=development
        DATABASE_URL=postgresql://postgres:postgres@localhost:5432/{n.ToLowerInvariant()}
        """;

    private static string TsIndex(string n) => $$"""
        import 'dotenv/config';
        import { app } from './app';

        const PORT = parseInt(process.env['PORT'] ?? '3000', 10);

        app.listen(PORT, () => {
          console.log(`[{{n}}] Listening on http://localhost:${PORT}`);
        });
        """;

    private static string TsApp(string n) => $$"""
        import express from 'express';
        import { errorHandler } from './middleware/errorHandler';
        import { {{Camel(n)}}Router } from './controllers/{{Camel(n)}}.controller';

        export const app = express();
        app.use(express.json());
        app.use(express.urlencoded({ extended: true }));

        app.get('/health', (_req, res) => res.json({ status: 'healthy', service: '{{n}}' }));
        app.use('/api/{{Camel(n).ToLowerInvariant()}}', {{Camel(n)}}Router);
        app.use(errorHandler);
        """;

    private static string TsModel(string n, string desc) => $$"""
        // {{desc}}
        export interface {{n}}Entity {
          id:          string;
          name:        string;
          description: string;
          status:      'Active' | 'Inactive';
          createdAt:   Date;
          updatedAt?:  Date;
        }

        export type Create{{n}}Dto = Pick<{{n}}Entity, 'name' | 'description'>;
        """;

    private static string TsDatabase(string n) => $$"""
        import { Pool } from 'pg';

        export const pool = new Pool({ connectionString: process.env['DATABASE_URL'] });

        export async function ensureTable(): Promise<void> {
          await pool.query(`
            CREATE TABLE IF NOT EXISTS {{n.ToLowerInvariant()}} (
              id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
              name        VARCHAR(500) NOT NULL,
              description TEXT NOT NULL DEFAULT '',
              status      VARCHAR(50)  NOT NULL DEFAULT 'Active',
              created_at  TIMESTAMPTZ  NOT NULL DEFAULT now(),
              updated_at  TIMESTAMPTZ
            )
          `);
        }
        """;

    private static string TsErrorHandler() => """
        import { Request, Response, NextFunction } from 'express';

        export function errorHandler(
          err: Error,
          _req: Request,
          res: Response,
          _next: NextFunction,
        ): void {
          console.error(err.stack);
          res.status(500).json({ error: err.message });
        }
        """;

    // ── Python templates ──────────────────────────────────────────────────────

    private static string PyRequirements() => """
        fastapi==0.111.0
        uvicorn[standard]==0.30.0
        sqlalchemy==2.0.30
        asyncpg==0.29.0
        alembic==1.13.1
        pydantic==2.7.0
        pydantic-settings==2.3.0
        python-dotenv==1.0.1
        """;

    private static string PyProject(string n, string desc) => $$"""
        [build-system]
        requires = ["setuptools>=68", "wheel"]
        build-backend = "setuptools.backends.legacy:build"

        [project]
        name = "{{n.ToLowerInvariant()}}"
        version = "0.1.0"
        description = "{{desc.Replace("\"", "\\\"")}}"
        requires-python = ">=3.12"
        """;

    private static string PyEnvExample(string n) => $"""
        DATABASE_URL=postgresql+asyncpg://postgres:postgres@localhost:5432/{n.ToLowerInvariant()}
        DEBUG=true
        """;

    private static string PyMain(string n, string snake) => $$"""
        from fastapi import FastAPI
        from dotenv import load_dotenv
        from src.{{snake}}.router import router

        load_dotenv()

        app = FastAPI(title="{{n}}", version="0.1.0")
        app.include_router(router, prefix="/api/{{snake}}", tags=["{{n}}"])


        @app.get("/health")
        async def health():
            return {"status": "healthy", "service": "{{n}}"}
        """;

    private static string PyModel(string n, string desc) => $$"""
        from datetime import datetime
        from uuid import UUID, uuid4
        from pydantic import BaseModel, Field


        class {{n}}Entity(BaseModel):
            '{{desc}}'
            id:          UUID     = Field(default_factory=uuid4)
            name:        str
            description: str      = ""
            status:      str      = "Active"
            created_at:  datetime = Field(default_factory=datetime.utcnow)
            updated_at:  datetime | None = None

            class Config:
                from_attributes = True


        class Create{{n}}Dto(BaseModel):
            name:        str
            description: str = ""
        """;

    private static string PyDatabase(string n) => $$"""
        import os
        from sqlalchemy.ext.asyncio import AsyncSession, create_async_engine, async_sessionmaker

        DATABASE_URL = os.getenv("DATABASE_URL", "postgresql+asyncpg://postgres:postgres@localhost/{{n.ToLowerInvariant()}}")

        engine  = create_async_engine(DATABASE_URL, echo=False)
        Session = async_sessionmaker(engine, expire_on_commit=False, class_=AsyncSession)


        async def get_session():
            async with Session() as session:
                yield session
        """;

    private static string PyRouter(string n, string snake) => $$"""
        from fastapi import APIRouter, Depends
        from .models import {{n}}Entity, Create{{n}}Dto
        from .database import get_session

        router = APIRouter()


        @router.get("/", response_model=list[{{n}}Entity])
        async def list_items(session=Depends(get_session)):
            return []


        @router.post("/", response_model={{n}}Entity, status_code=201)
        async def create_item(dto: Create{{n}}Dto, session=Depends(get_session)):
            return {{n}}Entity(name=dto.name, description=dto.description)
        """;

    private static string PyAlembicIni() => """
        [alembic]
        script_location = alembic
        file_template = %%(year)d%%(month).2d%%(day).2d_%%(rev)s_%%(slug)s
        sqlalchemy.url = postgresql+asyncpg://postgres:postgres@localhost/app

        [loggers]
        keys = root,sqlalchemy,alembic

        [handlers]
        keys = console

        [formatters]
        keys = generic
        """;

    // ── Java / Spring Boot templates ──────────────────────────────────────────

    private static string JavaPom(string n, string desc, string pkg) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <project xmlns="http://maven.apache.org/POM/4.0.0"
                 xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                 xsi:schemaLocation="http://maven.apache.org/POM/4.0.0 https://maven.apache.org/xsd/maven-4.0.0.xsd">
          <modelVersion>4.0.0</modelVersion>
          <parent>
            <groupId>org.springframework.boot</groupId>
            <artifactId>spring-boot-starter-parent</artifactId>
            <version>3.3.0</version>
          </parent>
          <groupId>{{pkg.Split('.')[0]}}.{{pkg.Split('.')[1]}}</groupId>
          <artifactId>{{n.ToLowerInvariant()}}</artifactId>
          <version>0.0.1-SNAPSHOT</version>
          <name>{{n}}</name>
          <description>{{desc.Replace("<", "&lt;")}}</description>
          <properties>
            <java.version>21</java.version>
          </properties>
          <dependencies>
            <dependency><groupId>org.springframework.boot</groupId><artifactId>spring-boot-starter-web</artifactId></dependency>
            <dependency><groupId>org.springframework.boot</groupId><artifactId>spring-boot-starter-data-jpa</artifactId></dependency>
            <dependency><groupId>org.postgresql</groupId><artifactId>postgresql</artifactId><scope>runtime</scope></dependency>
            <dependency><groupId>org.springframework.boot</groupId><artifactId>spring-boot-starter-test</artifactId><scope>test</scope></dependency>
          </dependencies>
          <build>
            <plugins>
              <plugin><groupId>org.springframework.boot</groupId><artifactId>spring-boot-maven-plugin</artifactId></plugin>
            </plugins>
          </build>
        </project>
        """;

    private static string JavaApp(string n, string pkg) => $$"""
        package {{pkg}};

        import org.springframework.boot.SpringApplication;
        import org.springframework.boot.autoconfigure.SpringBootApplication;

        @SpringBootApplication
        public class {{n}}Application {
            public static void main(String[] args) {
                SpringApplication.run({{n}}Application.class, args);
            }
        }
        """;

    private static string JavaModel(string n, string desc, string pkg) => $$"""
        package {{pkg}}.model;

        import jakarta.persistence.*;
        import java.time.Instant;
        import java.util.UUID;

        /** {{desc}} */
        @Entity
        @Table(name = "{{n.ToLowerInvariant()}}_items")
        public class {{n}} {
            @Id
            @GeneratedValue(strategy = GenerationType.UUID)
            private UUID id;

            @Column(nullable = false, length = 500)
            private String name;

            @Column(columnDefinition = "TEXT")
            private String description = "";

            @Column(nullable = false, length = 50)
            private String status = "Active";

            @Column(updatable = false)
            private Instant createdAt = Instant.now();

            private Instant updatedAt;

            // Getters & setters omitted for brevity — use Lombok @Data in production
            public UUID getId()          { return id; }
            public String getName()      { return name; }
            public void setName(String v){ this.name = v; }
            public String getStatus()    { return status; }
        }
        """;

    private static string JavaRepo(string n, string pkg) => $$"""
        package {{pkg}}.repository;

        import {{pkg}}.model.{{n}};
        import org.springframework.data.jpa.repository.JpaRepository;
        import java.util.UUID;

        public interface {{n}}Repository extends JpaRepository<{{n}}, UUID> {
        }
        """;

    private static string JavaController(string n, string pkg) => $$"""
        package {{pkg}}.controller;

        import {{pkg}}.model.{{n}};
        import {{pkg}}.repository.{{n}}Repository;
        import org.springframework.http.HttpStatus;
        import org.springframework.web.bind.annotation.*;
        import java.util.List;

        @RestController
        @RequestMapping("/api/{{n.ToLowerInvariant()}}")
        public class {{n}}Controller {

            private final {{n}}Repository repo;

            public {{n}}Controller({{n}}Repository repo) { this.repo = repo; }

            @GetMapping
            public List<{{n}}> list() { return repo.findAll(); }

            @PostMapping
            @ResponseStatus(HttpStatus.CREATED)
            public {{n}} create(@RequestBody {{n}} body) { return repo.save(body); }

            @GetMapping("/{id}")
            public {{n}} get(@PathVariable java.util.UUID id) {
                return repo.findById(id).orElseThrow();
            }
        }
        """;

    private static string JavaAppYml(string n) => $$"""
        spring:
          application:
            name: {{n}}
          datasource:
            url: jdbc:postgresql://localhost:5432/{{n.ToLowerInvariant()}}
            username: postgres
            password: postgres
          jpa:
            hibernate:
              ddl-auto: update
            show-sql: false
        server:
          port: 8080
        """;

    // ── Go templates ──────────────────────────────────────────────────────────

    private static string GoMod(string pkg) => $$"""
        module github.com/meridian/{{pkg}}

        go 1.22

        require (
            github.com/jackc/pgx/v5 v5.6.0
            github.com/go-chi/chi/v5 v5.0.12
            github.com/joho/godotenv v1.5.1
        )
        """;

    private static string GoMain(string pkg, string n) => $$"""
        package main

        import (
            "fmt"
            "log"
            "net/http"
            "os"

            "github.com/go-chi/chi/v5"
            "github.com/joho/godotenv"
            "github.com/meridian/{{pkg}}/internal/{{pkg}}"
            "github.com/meridian/{{pkg}}/internal/database"
        )

        func main() {
            _ = godotenv.Load()

            db, err := database.Connect(os.Getenv("DATABASE_URL"))
            if err != nil {
                log.Fatalf("db: %v", err)
            }
            defer db.Close()

            h := {{pkg}}.NewHandler(db)
            r := chi.NewRouter()
            r.Get("/health", func(w http.ResponseWriter, _ *http.Request) {
                fmt.Fprintf(w, `{"status":"healthy","service":"{{n}}"}`)
            })
            r.Mount("/api/{{pkg}}", h.Routes())

            port := os.Getenv("PORT")
            if port == "" {
                port = "8080"
            }
            log.Printf("[{{n}}] listening on :%s", port)
            log.Fatal(http.ListenAndServe(":"+port, r))
        }
        """;

    private static string GoModel(string pkg, string n, string desc) => $$"""
        package {{pkg}}

        import (
            "time"
        )

        // {{n}} — {{desc}}
        type {{n}} struct {
            ID          string    `json:"id"          db:"id"`
            Name        string    `json:"name"        db:"name"`
            Description string    `json:"description" db:"description"`
            Status      string    `json:"status"      db:"status"`
            CreatedAt   time.Time `json:"createdAt"   db:"created_at"`
        }

        type Create{{n}}Request struct {
            Name        string `json:"name"`
            Description string `json:"description"`
        }
        """;

    private static string GoRepo(string pkg, string n) => $$"""
        package {{pkg}}

        import (
            "context"
            "github.com/jackc/pgx/v5/pgxpool"
        )

        type Repository struct { db *pgxpool.Pool }

        func NewRepository(db *pgxpool.Pool) *Repository { return &Repository{db: db} }

        func (r *Repository) GetAll(ctx context.Context) ([]{{n}}, error) {
            rows, err := r.db.Query(ctx,
                `SELECT id, name, description, status, created_at FROM {{pkg}}_items ORDER BY created_at DESC`)
            if err != nil {
                return nil, err
            }
            defer rows.Close()
            var items []{{n}}
            for rows.Next() {
                var item {{n}}
                if err := rows.Scan(&item.ID, &item.Name, &item.Description, &item.Status, &item.CreatedAt); err != nil {
                    return nil, err
                }
                items = append(items, item)
            }
            return items, nil
        }
        """;

    private static string GoHandler(string pkg, string n) => $$"""
        package {{pkg}}

        import (
            "encoding/json"
            "net/http"

            "github.com/go-chi/chi/v5"
            "github.com/jackc/pgx/v5/pgxpool"
        )

        type Handler struct { repo *Repository }

        func NewHandler(db *pgxpool.Pool) *Handler { return &Handler{repo: NewRepository(db)} }

        func (h *Handler) Routes() chi.Router {
            r := chi.NewRouter()
            r.Get("/",  h.list)
            r.Post("/", h.create)
            return r
        }

        func (h *Handler) list(w http.ResponseWriter, r *http.Request) {
            items, err := h.repo.GetAll(r.Context())
            if err != nil {
                http.Error(w, err.Error(), http.StatusInternalServerError)
                return
            }
            w.Header().Set("Content-Type", "application/json")
            json.NewEncoder(w).Encode(items)
        }

        func (h *Handler) create(w http.ResponseWriter, r *http.Request) {
            var req Create{{n}}Request
            if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
                http.Error(w, err.Error(), http.StatusBadRequest)
                return
            }
            w.Header().Set("Content-Type", "application/json")
            w.WriteHeader(http.StatusCreated)
            json.NewEncoder(w).Encode({{n}}{Name: req.Name, Description: req.Description, Status: "Active"})
        }
        """;

    private static string GoDatabase(string pkg) => $$"""
        package database

        import (
            "context"
            "github.com/jackc/pgx/v5/pgxpool"
        )

        func Connect(dsn string) (*pgxpool.Pool, error) {
            if dsn == "" {
                dsn = "postgresql://postgres:postgres@localhost:5432/{{pkg}}"
            }
            return pgxpool.New(context.Background(), dsn)
        }
        """;

    private static string GoConfig(string pkg, string n) => $$"""
        package config

        import "os"

        type Config struct {
            Port        string
            DatabaseURL string
        }

        func Load() Config {
            return Config{
                Port:        getenv("PORT", "8080"),
                DatabaseURL: getenv("DATABASE_URL", "postgresql://postgres:postgres@localhost/{{pkg}}"),
            }
        }

        func getenv(key, fallback string) string {
            if v := os.Getenv(key); v != "" {
                return v
            }
            return fallback
        }
        """;

    private static string GoEnvExample(string n) => $"""
        PORT=8080
        DATABASE_URL=postgresql://postgres:postgres@localhost:5432/{n.ToLowerInvariant()}
        """;

    // ── Shared templates ──────────────────────────────────────────────────────

    private static string DockerCompose(string n) => $$"""
        services:
          db:
            image: postgres:16-alpine
            environment:
              POSTGRES_DB: {{n.ToLowerInvariant()}}
              POSTGRES_USER: postgres
              POSTGRES_PASSWORD: postgres
            ports:
              - "5432:5432"
            volumes:
              - pgdata:/var/lib/postgresql/data

          api:
            build: .
            depends_on: [db]
            ports:
              - "8080:8080"
            environment:
              - ConnectionStrings__Default=Host=db;Database={{n.ToLowerInvariant()}};Username=postgres;Password=postgres

        volumes:
          pgdata:
        """;

    private static string Readme(
        string n, string desc, GenerateProjectRequest req, string[] steps,
        string lang, string runCmd, string[] prereqs)
    {
        var stepsSection = steps.Length > 0
            ? "## Integration Steps\n\n" + string.Join("\n", steps.Select((s, i) => $"{i + 1}. {s}"))
            : string.Empty;

        var prereqList = string.Join("\n", prereqs.Select(p => $"- {p}"));

        return $$"""
            # {{n}}

            > **{{desc}}**

            Generated by [MeridianStudio](https://meridianstudio.ai) · Language: {{lang}}{{(req.Domain is not null ? $" · Domain: {req.Domain}" : "")}}

            ---

            ## Prerequisites

            {{prereqList}}

            ## Getting Started

            ```bash
            {{runCmd}}
            ```

            {{stepsSection}}

            ## Project Structure

            Each integration step has its own service file in the Application/Services (or equivalent) layer.
            Update the namespace declarations if they don't match your project conventions.
            The domain model, repository, and database context are ready to use — wire them up in
            your DI container and run the database migration before starting.

            ## Environment Variables

            Copy `.env.example` → `.env` and fill in your database credentials before running.

            ## Next Steps

            1. Run the database migration / `docker-compose up db`
            2. Review and adjust the generated service code in each `Step*.cs` / `step*.ts` / `step*.py` file
            3. Register service dependencies in your DI container
            4. Add authentication / authorisation middleware
            5. Deploy using the provided `docker-compose.yml`

            ---
            *Generated on {{DateTimeOffset.UtcNow:yyyy-MM-dd}} by MeridianStudio*
            """;
    }

    private static string DotnetGitignore() => """
        obj/
        bin/
        .vs/
        *.user
        .env
        *.log
        TestResults/
        """;

    private static string NodeGitignore() => """
        node_modules/
        dist/
        .env
        *.log
        coverage/
        """;

    private static string JavaGitignore() => """
        target/
        .idea/
        *.class
        .env
        *.log
        """;

    private static string PythonGitignore() => """
        __pycache__/
        *.pyc
        .venv/
        .env
        *.egg-info/
        dist/
        """;

    private static string GoGitignore() => """
        *.exe
        *.exe~
        *.so
        .env
        vendor/
        """;

    // ── Zip helper ────────────────────────────────────────────────────────────

    private static byte[] Zip(List<(string Path, string Content)> files, string projectName)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in files)
            {
                var entry = archive.CreateEntry(
                    $"{projectName}/{path}".Replace('\\', '/'),
                    CompressionLevel.Optimal);
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write(content);
            }
        }
        return ms.ToArray();
    }

    // ── Name helpers ──────────────────────────────────────────────────────────

    private static string SafeName(string raw)
    {
        var words = Regex.Split(raw.Trim(), @"[\s\-_\.]+")
                         .Where(w => w.Length > 0
                             && !string.Equals(w, "AI",   StringComparison.OrdinalIgnoreCase)
                             && !string.Equals(w, "ML",   StringComparison.OrdinalIgnoreCase)
                             && !string.Equals(w, "The",  StringComparison.OrdinalIgnoreCase)
                             && !string.Equals(w, "A",    StringComparison.OrdinalIgnoreCase))
                         .Take(4)
                         .Select(w => char.ToUpper(w[0]) + (w.Length > 1 ? w[1..] : string.Empty));
        var name = string.Concat(words);
        return name.Length == 0 ? "MeridianApp" : name;
    }

    private static string Camel(string n) =>
        n.Length == 0 ? n : char.ToLower(n[0]) + n[1..];

    private static string Snake(string n) =>
        Regex.Replace(n, @"(?<!^)[A-Z]", m => "_" + m.Value).ToLowerInvariant();

    private static string NewG() => Guid.NewGuid().ToString("B").ToUpper();
}
