using Microsoft.EntityFrameworkCore;

namespace Arqenion.Api.Data;

// Açılışta şema: Postgres'te EF Core migrations (Data/Migrations), bellek içi veritabanında EnsureCreated.
// Migrations öncesi EnsureCreated ile kurulmuş veritabanı (tablolar var, geçmiş tablosu yok) önce temel sürüme
// getirilir ve "Baseline uygulandı" diye işaretlenir; veri silinmez.
public static class DatabaseMigrator
{
    public const string BaselineMigration = "20261014064608_Baseline";
    private const string EfProductVersion = "8.0.8";

    // Temel sürümdeki, eski (EnsureCreated) veritabanında olmayabilecek kolon ve indeksler. Hepsi tekrar çalıştırılabilir.
    private const string LegacyToBaselineSql = """
        ALTER TABLE "Sessions" ADD COLUMN IF NOT EXISTS "ProcedureModel" text NULL;
        ALTER TABLE "Sessions" ADD COLUMN IF NOT EXISTS "CurrentNodeId" text NULL;
        ALTER TABLE "Sessions" ADD COLUMN IF NOT EXISTS "FlowStateJson" text NULL;
        ALTER TABLE "SessionEvents" ADD COLUMN IF NOT EXISTS "NodeId" text NULL;
        ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "Username" text NULL;
        ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "PasswordHash" text NULL;
        ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "Role" text NOT NULL DEFAULT 'trainee';
        ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "IsActive" boolean NOT NULL DEFAULT true;
        ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "CreatedAt" timestamp with time zone NOT NULL DEFAULT now();
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_Users_Username" ON "Users" ("Username");
        CREATE INDEX IF NOT EXISTS "IX_AiFeedbacks_SessionId" ON "AiFeedbacks" ("SessionId");
        CREATE INDEX IF NOT EXISTS "IX_SessionEvents_SessionId" ON "SessionEvents" ("SessionId");
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_SessionEvents_SessionId_EventId" ON "SessionEvents" ("SessionId", "EventId");
        """;

    public static async Task MigrateAsync(ArqenionDbContext db, ILogger logger)
    {
        if (!db.Database.IsRelational())
        {
            await db.Database.EnsureCreatedAsync();
            return;
        }

        var legacy = await ExistsAsync(db, "Sessions") && !await ExistsAsync(db, "__EFMigrationsHistory");
        if (legacy)
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlRawAsync(LegacyToBaselineSql);
            await db.Database.ExecuteSqlRawAsync($"""
                CREATE TABLE "__EFMigrationsHistory" ("MigrationId" character varying(150) NOT NULL PRIMARY KEY,
                                                     "ProductVersion" character varying(32) NOT NULL);
                INSERT INTO "__EFMigrationsHistory" VALUES ('{BaselineMigration}', '{EfProductVersion}');
                """);
            await tx.CommitAsync();
            logger.LogWarning("Eski (EnsureCreated) veritabanı temel sürüme getirildi ve {Migration} uygulandı sayıldı; veri korundu.",
                BaselineMigration);
        }

        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        if (pending.Count > 0) logger.LogInformation("Uygulanacak migration: {Migrations}", string.Join(", ", pending));
        await db.Database.MigrateAsync();
    }

    private static async Task<bool> ExistsAsync(ArqenionDbContext db, string table)
        => await db.Database.SqlQueryRaw<bool>("SELECT to_regclass({0}) IS NOT NULL AS \"Value\"", $"public.\"{table}\"").SingleAsync();
}
