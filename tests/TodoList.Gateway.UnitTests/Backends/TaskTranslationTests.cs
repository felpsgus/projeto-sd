using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
using TodoList.Gateway.Api.Backends;
using TodoList.Gateway.Api.Contracts;
using Xunit;
using ProtoListTasksReply = TodoList.Contracts.Tasks.V1.ListTasksReply;
using ProtoTaskPriority = TodoList.Contracts.Tasks.V1.TaskPriority;
using ProtoTaskReply = TodoList.Contracts.Tasks.V1.TaskReply;
using ProtoTaskStatus = TodoList.Contracts.Tasks.V1.TaskStatus;
using ProtoTaskStatusFilter = TodoList.Contracts.Tasks.V1.TaskStatusFilter;

namespace TodoList.Gateway.UnitTests.Backends;

/// <summary>BE-36, CA-04/CA-10 — tradução DTO HTTP ↔ proto, isolada de qualquer cliente/servidor gRPC.</summary>
public class TaskTranslationTests
{
    [Fact]
    public void ToProtoRequest_PrioridadeAusente_VirmaUnspecified()
    {
        var proto = TaskTranslation.ToProtoRequest(new CreateTaskHttpRequest("Título", null, null, null));

        proto.Priority.Should().Be(ProtoTaskPriority.Unspecified);
        proto.HasDescription.Should().BeFalse();
        proto.HasDueDate.Should().BeFalse();
    }

    [Theory]
    [InlineData("Low", ProtoTaskPriority.Low)]
    [InlineData("MEDIUM", ProtoTaskPriority.Medium)]
    [InlineData("high", ProtoTaskPriority.High)]
    public void ToProtoRequest_PrioridadeInformada_MapeiaCaseInsensitive(string priority, ProtoTaskPriority expected)
    {
        var proto = TaskTranslation.ToProtoRequest(new CreateTaskHttpRequest("Título", null, priority, null));

        proto.Priority.Should().Be(expected);
    }

    [Fact]
    public void ToProtoRequest_DescricaoEDueDateInformados_SetamOsCamposOptional()
    {
        var proto = TaskTranslation.ToProtoRequest(new CreateTaskHttpRequest("Título", "Descrição", "Low", "2026-12-31"));

        proto.HasDescription.Should().BeTrue();
        proto.Description.Should().Be("Descrição");
        proto.HasDueDate.Should().BeTrue();
        proto.DueDate.Should().Be("2026-12-31");
    }

    [Fact]
    public void ToProtoRequest_Titulo_EhTrimado()
    {
        var proto = TaskTranslation.ToProtoRequest(new CreateTaskHttpRequest("  Título  ", null, null, null));

        proto.Title.Should().Be("Título");
    }

    [Fact]
    public void ToHttpResponse_ReplyCompleto_TraduzTodosOsCampos()
    {
        var now = Timestamp.FromDateTime(DateTime.UtcNow);
        var reply = new ProtoTaskReply
        {
            Id = "11111111-1111-1111-1111-111111111111",
            Title = "Título",
            Description = "Descrição",
            Priority = ProtoTaskPriority.High,
            Status = ProtoTaskStatus.Completed,
            DueDate = "2026-12-31",
            IsOverdue = false,
            CompletedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };

        var response = TaskTranslation.ToHttpResponse(reply);

        response.Id.Should().Be(reply.Id);
        response.Title.Should().Be("Título");
        response.Description.Should().Be("Descrição");
        response.Priority.Should().Be("High");
        response.Status.Should().Be("Completed");
        response.DueDate.Should().Be("2026-12-31");
        response.CompletedAt.Should().NotBeNull();
        response.IsOverdue.Should().BeFalse();
    }

    [Fact]
    public void ToHttpResponse_SemDescricaoDueDateOuCompletedAt_CamposViramNull()
    {
        var now = Timestamp.FromDateTime(DateTime.UtcNow);
        var reply = new ProtoTaskReply
        {
            Id = "11111111-1111-1111-1111-111111111111",
            Title = "Título",
            Priority = ProtoTaskPriority.Medium,
            Status = ProtoTaskStatus.Pending,
            IsOverdue = false,
            CreatedAt = now,
            UpdatedAt = now,
        };

        var response = TaskTranslation.ToHttpResponse(reply);

        response.Description.Should().BeNull();
        response.DueDate.Should().BeNull();
        response.CompletedAt.Should().BeNull();
        response.Status.Should().Be("Pending");
    }

    [Fact] // BE-41, CA-17
    public void ToProtoRequest_ListTasksHttpRequest_CopiaPageEPageSize()
    {
        var proto = TaskTranslation.ToProtoRequest(new ListTasksHttpRequest(2, 50, "all", [], null, null));

        proto.Page.Should().Be(2);
        proto.PageSize.Should().Be(50);
    }

    [Theory] // BE-22, RN-LIST-02
    [InlineData("pending", ProtoTaskStatusFilter.Pending)]
    [InlineData("completed", ProtoTaskStatusFilter.Completed)]
    [InlineData("all", ProtoTaskStatusFilter.All)]
    public void ToProtoRequest_Status_Mapeia(string status, ProtoTaskStatusFilter expected)
    {
        var proto = TaskTranslation.ToProtoRequest(new ListTasksHttpRequest(1, 20, status, [], null, null));

        proto.Status.Should().Be(expected);
    }

    [Fact] // BE-22, RN-LIST-03, CA-07
    public void ToProtoRequest_PrioridadesMultiplas_CopiaNaOrdemInformada()
    {
        var proto = TaskTranslation.ToProtoRequest(new ListTasksHttpRequest(1, 20, "all", ["low", "high"], null, null));

        proto.Priority.Should().Equal(ProtoTaskPriority.Low, ProtoTaskPriority.High);
    }

    [Fact] // BE-22, RN-LIST-03 — lista vazia não define nenhum elemento
    public void ToProtoRequest_PrioridadeAusente_ListaProtoVazia()
    {
        var proto = TaskTranslation.ToProtoRequest(new ListTasksHttpRequest(1, 20, "all", [], null, null));

        proto.Priority.Should().BeEmpty();
    }

    [Fact] // BE-22, RN-LIST-04 — "não informado" nunca define o campo optional
    public void ToProtoRequest_OverdueAusente_HasOverdueFalso()
    {
        var proto = TaskTranslation.ToProtoRequest(new ListTasksHttpRequest(1, 20, "all", [], null, null));

        proto.HasOverdue.Should().BeFalse();
    }

    [Theory] // BE-22, RN-LIST-04 — valor explícito, inclusive "false", define o campo
    [InlineData(true)]
    [InlineData(false)]
    public void ToProtoRequest_OverdueInformado_DefineOCampoExplicitamente(bool overdue)
    {
        var proto = TaskTranslation.ToProtoRequest(new ListTasksHttpRequest(1, 20, "all", [], overdue, null));

        proto.HasOverdue.Should().BeTrue();
        proto.Overdue.Should().Be(overdue);
    }

    [Fact] // BE-22, RN-LIST-05, CA-17 — ausente vira string vazia (equivalente do proto)
    public void ToProtoRequest_SearchAusente_StringVazia()
    {
        var proto = TaskTranslation.ToProtoRequest(new ListTasksHttpRequest(1, 20, "all", [], null, null));

        proto.Search.Should().BeEmpty();
    }

    [Fact] // BE-22, RN-LIST-05
    public void ToProtoRequest_SearchInformado_CopiaOTexto()
    {
        var proto = TaskTranslation.ToProtoRequest(new ListTasksHttpRequest(1, 20, "all", [], null, "relatório"));

        proto.Search.Should().Be("relatório");
    }

    [Fact] // BE-41, CA-17/CA-24
    public void ToHttpResponse_ListTasksReply_TraduzItensEPaginacao()
    {
        var now = Timestamp.FromDateTime(DateTime.UtcNow);
        var reply = new ProtoListTasksReply { Page = 1, PageSize = 20, TotalCount = 1 };
        reply.Items.Add(new ProtoTaskReply
        {
            Id = "11111111-1111-1111-1111-111111111111",
            Title = "Título",
            Priority = ProtoTaskPriority.Medium,
            Status = ProtoTaskStatus.Pending,
            IsOverdue = false,
            CreatedAt = now,
            UpdatedAt = now,
        });

        var response = TaskTranslation.ToHttpResponse(reply);

        response.Page.Should().Be(1);
        response.PageSize.Should().Be(20);
        response.TotalCount.Should().Be(1);
        response.Items.Should().ContainSingle();
        response.Items[0].Title.Should().Be("Título");
    }

    [Fact] // BE-41, CA-17
    public void ToHttpResponse_ListTasksReplySemItens_DevolveListaVazia()
    {
        var reply = new ProtoListTasksReply { Page = 1, PageSize = 20, TotalCount = 0 };

        var response = TaskTranslation.ToHttpResponse(reply);

        response.Items.Should().BeEmpty();
    }

    [Fact] // BE-41, CA-20/CA-22
    public void ToProtoGetTaskRequest_CopiaOId()
    {
        var proto = TaskTranslation.ToProtoGetTaskRequest("11111111-1111-1111-1111-111111111111");

        proto.Id.Should().Be("11111111-1111-1111-1111-111111111111");
    }

    [Fact] // BE-19
    public void ToProtoRequest_UpdateTaskComPrioridadeAusente_VirmaUnspecified()
    {
        var proto = TaskTranslation.ToProtoRequest(
            "11111111-1111-1111-1111-111111111111", new UpdateTaskHttpRequest("Título", null, null, null));

        proto.Id.Should().Be("11111111-1111-1111-1111-111111111111");
        proto.Priority.Should().Be(ProtoTaskPriority.Unspecified);
        proto.HasDescription.Should().BeFalse();
        proto.HasDueDate.Should().BeFalse();
    }

    [Fact] // BE-19 — description/dueDate informados setam os campos optional
    public void ToProtoRequest_UpdateTaskComDescricaoEDueDateInformados_SetamOsCamposOptional()
    {
        var proto = TaskTranslation.ToProtoRequest(
            "11111111-1111-1111-1111-111111111111",
            new UpdateTaskHttpRequest("Título", "Descrição", "Low", "2026-12-31"));

        proto.HasDescription.Should().BeTrue();
        proto.Description.Should().Be("Descrição");
        proto.HasDueDate.Should().BeTrue();
        proto.DueDate.Should().Be("2026-12-31");
        proto.Priority.Should().Be(ProtoTaskPriority.Low);
    }

    [Fact] // BE-19 — título é trimado, mesmo comportamento de CreateTask
    public void ToProtoRequest_UpdateTaskTitulo_EhTrimado()
    {
        var proto = TaskTranslation.ToProtoRequest(
            "11111111-1111-1111-1111-111111111111", new UpdateTaskHttpRequest("  Título  ", null, null, null));

        proto.Title.Should().Be("Título");
    }

    [Fact] // BE-20
    public void ToProtoCompleteTaskRequest_CopiaOId()
    {
        var proto = TaskTranslation.ToProtoCompleteTaskRequest("11111111-1111-1111-1111-111111111111");

        proto.Id.Should().Be("11111111-1111-1111-1111-111111111111");
    }

    [Fact] // BE-20
    public void ToProtoReopenTaskRequest_CopiaOId()
    {
        var proto = TaskTranslation.ToProtoReopenTaskRequest("11111111-1111-1111-1111-111111111111");

        proto.Id.Should().Be("11111111-1111-1111-1111-111111111111");
    }

    [Fact] // BE-21
    public void ToProtoDeleteTaskRequest_CopiaOId()
    {
        var proto = TaskTranslation.ToProtoDeleteTaskRequest("11111111-1111-1111-1111-111111111111");

        proto.Id.Should().Be("11111111-1111-1111-1111-111111111111");
    }
}
