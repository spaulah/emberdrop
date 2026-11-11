using FluentMigrator;

namespace Emberdrop.Storage.SqlServer.Migrations;

/// <summary>
/// Creates the work unit tables. <c>WorkUnitDefinition</c> holds the stored work units, their plugin binding (plugin id, logical work unit name,
/// version constraint), the schema version of their input, and the plugin's default schedule kept apart from the operator's override.
/// The initial migration is the whole history; the project had no release when the work unit vocabulary was introduced.
/// </summary>
[Migration(1)]
public sealed class CreateWorkUnitTables : Migration
{
    /// <inheritdoc />
    public override void Up()
    {
        Create.Table("WorkUnitDefinition")
            .WithColumn("WorkUnitId").AsInt32().PrimaryKey().Identity()
            .WithColumn("Identifier").AsString(500).NotNullable()
            .WithColumn("Name").AsString(500).NotNullable()
            .WithColumn("Type").AsString(500).NotNullable()
            .WithColumn("Enabled").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("Schedule").AsString(50).NotNullable()
            .WithColumn("Input").AsString(int.MaxValue).Nullable()
            .WithColumn("CreatedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
            .WithColumn("UpdatedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
            .WithColumn("PluginId").AsString(100).Nullable()
            .WithColumn("WorkUnitName").AsString(100).Nullable()
            .WithColumn("VersionConstraint").AsString(50).Nullable()
            .WithColumn("InputSchemaVersion").AsInt32().NotNullable().WithDefaultValue(1)
            .WithColumn("DefaultSchedule").AsString(50).Nullable()
            .WithColumn("ScheduleOverride").AsString(50).Nullable()
            .WithColumn("Kind").AsString(50).NotNullable().WithDefaultValue("work-unit")
            .WithColumn("Description").AsString(1000).Nullable()
            .WithColumn("Capabilities").AsString(int.MaxValue).Nullable()
            .WithColumn("InputSchema").AsString(int.MaxValue).Nullable()
            .WithColumn("OutputSchema").AsString(int.MaxValue).Nullable();

        // Triggers an operator added to a stored work unit, kept apart from the triggers a plugin's manifest declares.
        Create.Table("WorkUnitTrigger")
            .WithColumn("TriggerId").AsInt32().PrimaryKey().Identity()
            .WithColumn("WorkUnitId").AsInt32().NotNullable()
            .WithColumn("Kind").AsString(50).NotNullable()
            .WithColumn("Definition").AsString(500).NotNullable()
            .WithColumn("Enabled").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("CreatedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);
        Create.Index("IX_WorkUnitTrigger_WorkUnitId").OnTable("WorkUnitTrigger").OnColumn("WorkUnitId").Ascending();

        // Not unique: work units that belong to no plugin all have a null PluginId, and uniqueness of (plugin, work unit) is kept by the reconciler.
        Create.Index("IX_WorkUnitDefinition_PluginId_WorkUnitName").OnTable("WorkUnitDefinition").OnColumn("PluginId").Ascending().OnColumn("WorkUnitName").Ascending();
    }

    /// <inheritdoc />
    public override void Down()
    {
        Delete.Table("WorkUnitTrigger");
        Delete.Table("WorkUnitDefinition");
    }
}
