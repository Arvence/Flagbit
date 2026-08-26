using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Flagbit.Infrastructure.Persistence.Migrations
{
    public partial class InitialPostgreSql : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "feature_flags",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    key = table.Column<string>(type: "text", nullable: false),
                    normalized_key = table.Column<string>(type: "text", nullable: false, computedColumnSql: "upper(\"key\")", stored: true),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    rollout_percentage = table.Column<int>(type: "integer", nullable: true),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feature_flags", x => x.id);
                    table.CheckConstraint("ck_feature_flags_rollout_percentage", "rollout_percentage IS NULL OR rollout_percentage BETWEEN 0 AND 100");
                    table.CheckConstraint("ck_feature_flags_schedule", "starts_at IS NULL OR ends_at IS NULL OR starts_at <= ends_at");
                });

            migrationBuilder.CreateTable(
                name: "feature_flag_dependencies",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    feature_flag_id = table.Column<long>(type: "bigint", nullable: false),
                    dependency_key = table.Column<string>(type: "text", nullable: false),
                    normalized_dependency_key = table.Column<string>(type: "text", nullable: false, computedColumnSql: "upper(\"dependency_key\")", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feature_flag_dependencies", x => x.id);
                    table.ForeignKey(
                        name: "FK_feature_flag_dependencies_feature_flags_feature_flag_id",
                        column: x => x.feature_flag_id,
                        principalTable: "feature_flags",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "feature_flag_environments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    feature_flag_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    normalized_name = table.Column<string>(type: "text", nullable: false, computedColumnSql: "upper(\"name\")", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feature_flag_environments", x => x.id);
                    table.ForeignKey(
                        name: "FK_feature_flag_environments_feature_flags_feature_flag_id",
                        column: x => x.feature_flag_id,
                        principalTable: "feature_flags",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "feature_flag_rules",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    feature_flag_id = table.Column<long>(type: "bigint", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    attribute = table.Column<string>(type: "text", nullable: false),
                    @operator = table.Column<string>(name: "operator", type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feature_flag_rules", x => x.id);
                    table.CheckConstraint("ck_feature_flag_rules_position", "position >= 0");
                    table.ForeignKey(
                        name: "FK_feature_flag_rules_feature_flags_feature_flag_id",
                        column: x => x.feature_flag_id,
                        principalTable: "feature_flags",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "feature_flag_target_users",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    feature_flag_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    normalized_user_id = table.Column<string>(type: "text", nullable: false, computedColumnSql: "upper(\"user_id\")", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feature_flag_target_users", x => x.id);
                    table.ForeignKey(
                        name: "FK_feature_flag_target_users_feature_flags_feature_flag_id",
                        column: x => x.feature_flag_id,
                        principalTable: "feature_flags",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_feature_flag_dependencies_flag_key",
                table: "feature_flag_dependencies",
                columns: new[] { "feature_flag_id", "normalized_dependency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_feature_flag_environments_flag_name",
                table: "feature_flag_environments",
                columns: new[] { "feature_flag_id", "normalized_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_feature_flag_rules_flag_position",
                table: "feature_flag_rules",
                columns: new[] { "feature_flag_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_feature_flag_target_users_flag_user",
                table: "feature_flag_target_users",
                columns: new[] { "feature_flag_id", "normalized_user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_feature_flags_normalized_key",
                table: "feature_flags",
                column: "normalized_key",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "feature_flag_dependencies");

            migrationBuilder.DropTable(
                name: "feature_flag_environments");

            migrationBuilder.DropTable(
                name: "feature_flag_rules");

            migrationBuilder.DropTable(
                name: "feature_flag_target_users");

            migrationBuilder.DropTable(
                name: "feature_flags");
        }
    }
}
