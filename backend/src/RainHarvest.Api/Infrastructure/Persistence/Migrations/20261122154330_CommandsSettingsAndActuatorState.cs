using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RainHarvest.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CommandsSettingsAndActuatorState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "irrigating",
                table: "devices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "pump_on",
                table: "devices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "state_reported_at",
                table: "devices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "valve_open",
                table: "devices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "device_commands",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    duration_s = table.Column<int>(type: "integer", nullable: true),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status_reason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_commands", x => x.id);
                    table.ForeignKey(
                        name: "fk_device_commands_devices_device_id",
                        column: x => x.device_id,
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "device_settings",
                columns: table => new
                {
                    device_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ph_min = table.Column<double>(type: "double precision", nullable: false),
                    ph_max = table.Column<double>(type: "double precision", nullable: false),
                    turbidity_max_ntu = table.Column<double>(type: "double precision", nullable: false),
                    level_min_pct = table.Column<double>(type: "double precision", nullable: false),
                    soil_dry_pct = table.Column<double>(type: "double precision", nullable: false),
                    soil_wet_pct = table.Column<double>(type: "double precision", nullable: false),
                    irrigation_duration_s = table.Column<int>(type: "integer", nullable: false),
                    irrigation_cooldown_s = table.Column<int>(type: "integer", nullable: false),
                    pump_max_s = table.Column<int>(type: "integer", nullable: false),
                    auto_irrigation = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_settings", x => x.device_id);
                    table.ForeignKey(
                        name: "fk_device_settings_devices_device_id",
                        column: x => x.device_id,
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_device_commands_created_at",
                table: "device_commands",
                column: "created_at",
                filter: "status = 'Sent'");

            migrationBuilder.CreateIndex(
                name: "ix_device_commands_device_id_created_at",
                table: "device_commands",
                columns: new[] { "device_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "device_commands");

            migrationBuilder.DropTable(
                name: "device_settings");

            migrationBuilder.DropColumn(
                name: "irrigating",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "pump_on",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "state_reported_at",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "valve_open",
                table: "devices");
        }
    }
}
