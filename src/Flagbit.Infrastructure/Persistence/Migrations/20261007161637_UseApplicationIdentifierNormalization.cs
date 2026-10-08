using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flagbit.Infrastructure.Persistence.Migrations
{
    public partial class UseApplicationIdentifierNormalization : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var source = new System.Text.StringBuilder();
            var target = new System.Text.StringBuilder();
            for (var codePoint = 0; codePoint <= 0x10ffff; codePoint++)
            {
                if (!System.Text.Rune.IsValid(codePoint))
                {
                    continue;
                }

                var value = char.ConvertFromUtf32(codePoint);
                var normalized = value.ToUpperInvariant();
                if (value != normalized && string.Equals(value, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    source.Append(value);
                    target.Append(normalized);
                }
            }

            migrationBuilder.Sql("""
                ALTER TABLE feature_flags ALTER COLUMN normalized_key DROP EXPRESSION;
                ALTER TABLE feature_flag_target_users ALTER COLUMN normalized_user_id DROP EXPRESSION;
                ALTER TABLE feature_flag_environments ALTER COLUMN normalized_name DROP EXPRESSION;
                ALTER TABLE feature_flag_dependencies ALTER COLUMN normalized_dependency_key DROP EXPRESSION;
                """);
            migrationBuilder.Sql($"""
                UPDATE feature_flags SET normalized_key = translate(key, '{source}', '{target}');
                UPDATE feature_flag_target_users SET normalized_user_id = translate(user_id, '{source}', '{target}');
                UPDATE feature_flag_environments SET normalized_name = translate(name, '{source}', '{target}');
                UPDATE feature_flag_dependencies SET normalized_dependency_key = translate(dependency_key, '{source}', '{target}');
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "normalized_key",
                table: "feature_flags",
                type: "text",
                nullable: false,
                computedColumnSql: "upper(\"key\")",
                stored: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "normalized_user_id",
                table: "feature_flag_target_users",
                type: "text",
                nullable: false,
                computedColumnSql: "upper(\"user_id\")",
                stored: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "normalized_name",
                table: "feature_flag_environments",
                type: "text",
                nullable: false,
                computedColumnSql: "upper(\"name\")",
                stored: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "normalized_dependency_key",
                table: "feature_flag_dependencies",
                type: "text",
                nullable: false,
                computedColumnSql: "upper(\"dependency_key\")",
                stored: true,
                oldClrType: typeof(string),
                oldType: "text");
        }
    }
}
