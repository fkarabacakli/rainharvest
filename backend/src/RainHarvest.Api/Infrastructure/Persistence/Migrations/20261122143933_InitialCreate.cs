using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RainHarvest.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:timescaledb", ",,");

            migrationBuilder.CreateTable(
                name: "devices",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    is_online = table.Column<bool>(type: "boolean", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_devices", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "readings",
                columns: table => new
                {
                    device_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    device_ts = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    level_pct = table.Column<double>(type: "double precision", nullable: false),
                    level_cm = table.Column<double>(type: "double precision", nullable: false),
                    turbidity_ntu = table.Column<double>(type: "double precision", nullable: false),
                    ph = table.Column<double>(type: "double precision", nullable: false),
                    water_temp_c = table.Column<double>(type: "double precision", nullable: true),
                    raining = table.Column<bool>(type: "boolean", nullable: false),
                    soil_pct = table.Column<double>(type: "double precision", nullable: false),
                    flow_lpm = table.Column<double>(type: "double precision", nullable: false),
                    flow_total_l = table.Column<double>(type: "double precision", nullable: false),
                    rssi = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_readings", x => new { x.device_id, x.received_at });
                    table.ForeignKey(
                        name: "fk_readings_devices_device_id",
                        column: x => x.device_id,
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            // TimescaleDB: partition readings by received_at in 1-day chunks.
            // EF Core does not model hypertables; see PLAN.md §7.
            migrationBuilder.Sql(
                "SELECT create_hypertable('readings', by_range('received_at', INTERVAL '1 day'));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "readings");

            migrationBuilder.DropTable(
                name: "devices");
        }
    }
}
