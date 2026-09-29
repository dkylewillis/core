using Core.Store;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Core.Store.Tests;

public class StoreTests
{
    private static readonly string[] ExpectedCoreTables =
    [
        "annotation", "attribute", "block_definition", "block_reference", "classification",
        "coverage_intent", "data_reference", "design_object", "design_relationship", "display_component",
        "document", "drawing", "entity_mapping", "geometry", "geometry_rtree", "layout", "layer", "meta",
        "presentation_instance", "profile_view", "project", "sheet", "sheet_rendition", "shortcut",
        "snapshot", "source_entity", "source_file", "style", "validation_message", "viewport",
        "viewport_frozen_layer", "xref_instance", "xref_path"
    ];

    private static readonly string[] ExpectedReviewTables =
    [
        "disposition", "evidence", "finding", "finding_observation", "meta", "observation",
        "rule", "rule_profile", "rule_run", "snapshot_ref"
    ];

    [Fact]
    public void CreateCoreStore_HasExpectedTables()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".core");
        try
        {
            using var store = CoreStore.Create(path, "snap-1", "proj-1", "MiniSite");
            var tables = store.ListUserTables();
            Assert.Equal(ExpectedCoreTables.OrderBy(x => x), tables.OrderBy(x => x));
            Assert.Equal("1", MigrationRunner.GetMeta(store.Connection, "schema_version"));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void CreateReviewStore_HasExpectedTables()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".corereview");
        try
        {
            using var store = ReviewStore.Create(path, "proj-1");
            var tables = store.ListUserTables();
            Assert.Equal(ExpectedReviewTables.OrderBy(x => x), tables.OrderBy(x => x));
            Assert.Equal("1", MigrationRunner.GetMeta(store.Connection, "schema_version"));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void CoreStore_OpensReadOnly_ForNonImporter()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".core");
        try
        {
            using (var created = CoreStore.Create(path, "snap-1", "proj-1", "MiniSite")) { }
            using var store = CoreStore.OpenReadOnly(path);
            Assert.True(store.IsReadOnly);
            using var cmd = store.Connection.CreateCommand();
            cmd.CommandText = "INSERT INTO meta(key, value) VALUES ('x','y');";
            Assert.ThrowsAny<Exception>(() => cmd.ExecuteNonQuery());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void UnsupportedSchemaVersion_FailsClearly()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".core");
        try
        {
            using (var created = CoreStore.Create(path, "snap-1", "proj-1", "MiniSite"))
            {
                MigrationRunner.SetMeta(created.Connection, "schema_version", "99");
            }

            var ex = Assert.Throws<StoreException>(() => CoreStore.OpenReadOnly(path));
            Assert.Contains("Unsupported", ex.Message);
            Assert.Contains("99", ex.Message);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
