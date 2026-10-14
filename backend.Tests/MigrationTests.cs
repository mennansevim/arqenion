using Arqenion.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;

namespace Arqenion.Api.Tests;

// Şema: EF Core migrations (backend/Data/Migrations).
public sealed class MigrationTests
{
    private static ArqenionDbContext Npgsql(string connection)
        => new(new DbContextOptionsBuilder<ArqenionDbContext>().UseNpgsql(connection).Options);

    // Entity'ye alan eklenip migration üretilmezse bu test kırılır:
    //   dotnet ef migrations add <Ad> --project backend --output-dir Data/Migrations -- --Database:Provider=Postgres
    [Fact]
    public void Model_matches_the_latest_migration()
    {
        using var db = Npgsql("Host=localhost;Database=yok");   // bağlantı açılmaz; yalnız model karşılaştırılır
        var snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot!.Model;
        var initialized = db.GetService<IModelRuntimeInitializer>().Initialize(snapshot);
        var diff = db.GetService<IMigrationsModelDiffer>()
            .GetDifferences(initialized.GetRelationalModel(), db.GetService<IDesignTimeModel>().Model.GetRelationalModel());
        Assert.True(diff.Count == 0, "Modelde migration'a yansımamış değişiklik var: " + string.Join(", ", diff.Select(d => d.GetType().Name)));
    }

    // Gerçek Postgres gerekir: ARQ_PG_TEST="Host=localhost;Port=55433;Database=…;Username=…;Password=…"
    // (veritabanı silinip yeniden kurulur; gerçek veritabanına vermeyin).
    private static string? PgTest => Environment.GetEnvironmentVariable("ARQ_PG_TEST");

    [Fact]
    public async Task Legacy_database_is_adopted_without_losing_data()
    {
        if (PgTest is null) return;
        await using var db = Npgsql(PgTest);
        await Reset(db);
        // Migrations öncesi (EnsureCreated) şema, 60 adım ve kimlik kolonları yok.
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE "Users" ("Id" text PRIMARY KEY, "Name" text NOT NULL, "Email" text NULL);
            CREATE TABLE "Sessions" ("Id" text PRIMARY KEY, "UserId" text NOT NULL, "ScenarioId" text NOT NULL, "ProcedureCode" text NOT NULL,
              "CurrentStepId" integer NULL, "Score" integer NOT NULL, "RawScore" integer NOT NULL, "Status" text NOT NULL,
              "CorrectActions" integer NOT NULL, "WrongActions" integer NOT NULL, "SterileViolations" integer NOT NULL, "HintsUsed" integer NOT NULL,
              "ConsecutiveErrors" integer NOT NULL, "ActiveComplicationId" text NULL, "ComplicationStepIndex" integer NOT NULL,
              "ComplicationsTriggered" integer NOT NULL, "ComplicationsResolved" integer NOT NULL,
              "StartedAt" timestamp with time zone NOT NULL, "CompletedAt" timestamp with time zone NULL);
            CREATE TABLE "SessionEvents" ("Id" text PRIMARY KEY, "SessionId" text NOT NULL, "EventId" text NOT NULL, "EventType" text NOT NULL,
              "StepId" integer NOT NULL, "InstrumentCode" text NULL, "IsSuccess" boolean NOT NULL, "DeviationType" text NULL,
              "ScoreChange" integer NOT NULL, "PayloadJson" text NULL, "CreatedAt" timestamp with time zone NOT NULL);
            CREATE TABLE "AiFeedbacks" ("Id" text PRIMARY KEY, "SessionId" text NOT NULL, "SessionEventId" text NULL, "StepId" integer NOT NULL,
              "DeviationType" text NULL, "PossibleRisk" text NOT NULL, "Explanation" text NOT NULL, "RecommendedAction" text NOT NULL,
              "Severity" text NOT NULL, "Source" text NOT NULL, "ModelName" text NULL, "PromptTokens" integer NULL, "CompletionTokens" integer NULL,
              "TotalTokens" integer NULL, "RequestJson" text NULL, "ResponseJson" text NULL, "CreatedAt" timestamp with time zone NOT NULL);
            INSERT INTO "Users" VALUES ('u1', 'u1', NULL);
            INSERT INTO "Sessions" VALUES ('eski', 'u1', 'case-basic-01', 'OPEN_APPENDECTOMY', 5, 87, 87, 'COMPLETED', 20, 3, 0, 1, 0, NULL, 0, 1, 1, now(), now());
            """);

        await DatabaseMigrator.MigrateAsync(db, NullLogger.Instance);
        await DatabaseMigrator.MigrateAsync(db, NullLogger.Instance);   // ikinci açılış da sorunsuz

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        var old = await db.Sessions.SingleAsync(s => s.Id == "eski");
        Assert.Equal(87, old.Score);
        Assert.Null(old.ProcedureModel);                                  // eski seans eski modelde kalır
        Assert.Equal("trainee", (await db.Users.SingleAsync()).Role);
    }

    [Fact]
    public async Task Empty_database_is_created_by_migrations()
    {
        if (PgTest is null) return;
        await using var db = Npgsql(PgTest);
        await Reset(db);
        await DatabaseMigrator.MigrateAsync(db, NullLogger.Instance);
        Assert.Equal(DatabaseMigrator.BaselineMigration, (await db.Database.GetAppliedMigrationsAsync()).First());
        db.Users.Add(new Models.User { Username = "a", Name = "a" });
        await db.SaveChangesAsync();
        db.Users.Add(new Models.User { Username = "a", Name = "b" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());   // kullanıcı adı tekil
    }

    private static Task Reset(ArqenionDbContext db)
        => db.Database.ExecuteSqlRawAsync("DROP SCHEMA public CASCADE; CREATE SCHEMA public;");
}
