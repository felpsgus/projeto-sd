using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TodoList.Tasks.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// FK cruzada entre schemas (BE-02, D-27, CA-02c/CA-15/CA-16):
    /// <c>tasks.tasks.owner_id → identity.users(id) ON DELETE CASCADE</c>.
    ///
    /// <para>
    /// Declarada por <b>SQL explícito</b>, não gerada a partir do modelo: o
    /// <c>TasksDbContext</c> nunca mapeia <c>identity.users</c> (D-27, CA-13),
    /// então o EF não tem como descobrir essa FK sozinho — <c>migrations add</c>
    /// produz um <c>Up</c>/<c>Down</c> vazios para esta migration, preenchidos
    /// aqui à mão.
    /// </para>
    ///
    /// <para>
    /// <b>Ordem de aplicação obrigatória: Identity primeiro.</b> Esta migration
    /// referencia <c>identity.users</c>, então o schema/tabela do Identity
    /// precisa existir antes dela ser aplicada — se alguém rodar
    /// <c>database update</c> do Tasks contra um banco vazio sem antes migrar o
    /// Identity, o Postgres rejeita com
    /// <c>ERRO: esquema "identity" não existe</c> (SQLSTATE <c>3F000</c>,
    /// confirmado contra Postgres real) — a FK não tem o que referenciar. Ver
    /// a seção "Migrations" do README.
    /// </para>
    /// </summary>
    public partial class AddOwnerForeignKeyToIdentityUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // CA-02c/CA-15: FK cruzando schemas com ON DELETE CASCADE — é o
            // que garante, no banco, que todo owner_id referencia um usuário
            // que existe (rede de segurança; a nota técnica de BE-02 deixa
            // claro que o caminho normal de rejeição é BE-28, não esta FK).
            migrationBuilder.Sql(
                """
                ALTER TABLE tasks.tasks
                    ADD CONSTRAINT fk_tasks_owner_id_identity_users
                    FOREIGN KEY (owner_id)
                    REFERENCES identity.users (id)
                    ON DELETE CASCADE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE tasks.tasks
                    DROP CONSTRAINT fk_tasks_owner_id_identity_users;
                """);
        }
    }
}
