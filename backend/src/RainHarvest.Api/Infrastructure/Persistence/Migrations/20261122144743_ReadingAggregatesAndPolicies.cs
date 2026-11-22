using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RainHarvest.Api.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// TimescaleDB objects EF Core does not model (PLAN.md §7):
    /// continuous aggregates readings_15m / readings_1h, columnstore compression
    /// after 7 days, and 90-day retention of raw readings.
    /// SQL is inlined on purpose: a migration is a frozen snapshot.
    /// </summary>
    public partial class ReadingAggregatesAndPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ContinuousAggregate("readings_15m", "15 minutes"));
            migrationBuilder.Sql(ContinuousAggregate("readings_1h", "1 hour"));

            // Refresh only recent windows; real-time aggregation (materialized_only = false)
            // fills in anything newer than the last refresh at query time.
            migrationBuilder.Sql(
                """
                SELECT add_continuous_aggregate_policy('readings_15m',
                    start_offset => INTERVAL '3 hours', end_offset => INTERVAL '15 minutes',
                    schedule_interval => INTERVAL '5 minutes');
                SELECT add_continuous_aggregate_policy('readings_1h',
                    start_offset => INTERVAL '3 days', end_offset => INTERVAL '1 hour',
                    schedule_interval => INTERVAL '30 minutes');
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE readings SET (
                    timescaledb.enable_columnstore = true,
                    timescaledb.segmentby = 'device_id',
                    timescaledb.orderby = 'received_at DESC');
                CALL add_columnstore_policy('readings', after => INTERVAL '7 days');
                """);

            // Raw data is kept 90 days; the aggregates are kept forever.
            // Refresh windows (3 h / 3 d) stay far inside the retention window.
            migrationBuilder.Sql("SELECT add_retention_policy('readings', drop_after => INTERVAL '90 days');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                SELECT remove_retention_policy('readings', if_exists => true);
                CALL remove_columnstore_policy('readings', if_exists => true);
                DROP MATERIALIZED VIEW IF EXISTS readings_1h;
                DROP MATERIALIZED VIEW IF EXISTS readings_15m;
                """);
        }

        private static string ContinuousAggregate(string name, string bucket) =>
            $"""
            CREATE MATERIALIZED VIEW {name}
            WITH (timescaledb.continuous, timescaledb.materialized_only = false) AS
            SELECT device_id,
                   time_bucket(INTERVAL '{bucket}', received_at) AS bucket,
                   count(*)                       AS sample_count,
                   avg(level_pct)                 AS level_pct_avg,
                   min(level_pct)                 AS level_pct_min,
                   max(level_pct)                 AS level_pct_max,
                   avg(ph)                        AS ph_avg,
                   min(ph)                        AS ph_min,
                   max(ph)                        AS ph_max,
                   avg(turbidity_ntu)             AS turbidity_ntu_avg,
                   min(turbidity_ntu)             AS turbidity_ntu_min,
                   max(turbidity_ntu)             AS turbidity_ntu_max,
                   avg(soil_pct)                  AS soil_pct_avg,
                   min(soil_pct)                  AS soil_pct_min,
                   max(soil_pct)                  AS soil_pct_max,
                   avg(water_temp_c)              AS water_temp_c_avg,
                   avg(flow_lpm)                  AS flow_lpm_avg,
                   max(flow_lpm)                  AS flow_lpm_max,
                   max(flow_total_l)              AS flow_total_l_max,
                   bool_or(raining)               AS raining,
                   avg(rssi)::double precision    AS rssi_avg
            FROM readings
            GROUP BY device_id, bucket
            WITH NO DATA;
            """;
    }
}
