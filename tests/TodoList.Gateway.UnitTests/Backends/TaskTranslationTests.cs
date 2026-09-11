using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
using TodoList.Gateway.Api.Backends;
using TodoList.Gateway.Api.Contracts;
using Xunit;
using ProtoTaskPriority = TodoList.Contracts.Tasks.V1.TaskPriority;
using ProtoTaskReply = TodoList.Contracts.Tasks.V1.TaskReply;
using ProtoTaskStatus = TodoList.Contracts.Tasks.V1.TaskStatus;

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
}
