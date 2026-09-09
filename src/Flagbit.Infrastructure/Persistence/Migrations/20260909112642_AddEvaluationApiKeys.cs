using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flagbit.Infrastructure.Persistence.Migrations
{
    public partial class AddEvaluationApiKeys : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "evaluation_api_keys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    key_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_evaluation_api_keys", x => x.id);
                });

            migrationBuilder.CreateIndex(name: "ux_evaluation_api_keys_key_hash", table: "evaluation_api_keys", column: "key_hash", unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "evaluation_api_keys");
        }
    }
}
