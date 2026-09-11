using FluentAssertions;
using TodoList.Tasks.Api.Grpc;
using TodoList.Tasks.Application.Tasks;
using TodoList.Tasks.Domain.Tasks;
using Xunit;
using ProtoCreateTaskRequest = TodoList.Contracts.Tasks.V1.CreateTaskRequest;
using ProtoTaskPriority = TodoList.Contracts.Tasks.V1.TaskPriority;

namespace TodoList.Tasks.UnitTests.Grpc;

/// <summary>
/// <see cref="TaskGrpcMapping"/> (BE-35) — tradução proto ↔ Application do
/// <c>CreateTask</c>, isolada de qualquer servidor gRPC real.
/// </summary>
public class TaskGrpcMappingTests
{
    [Fact] // TASK_PRIORITY_UNSPECIFIED vira null — o domínio aplica Média.
    public void ToApplicationRequest_ComPriorityUnspecified_MapeiaParaNull()
    {
        var request = new ProtoCreateTaskRequest { Title = "Tarefa", Priority = ProtoTaskPriority.Unspecified };

        var result = TaskGrpcMapping.ToApplicationRequest(request);

        result.IsValid.Should().BeTrue();
        result.Request!.Priority.Should().BeNull();
    }

    [Theory]
    [InlineData(ProtoTaskPriority.Low, TaskPriority.Low)]
    [InlineData(ProtoTaskPriority.Medium, TaskPriority.Medium)]
    [InlineData(ProtoTaskPriority.High, TaskPriority.High)]
    public void ToApplicationRequest_ComPriorityInformada_MapeiaParaODominioCorrespondente(
        ProtoTaskPriority protoPriority, TaskPriority prioridadeEsperada)
    {
        var request = new ProtoCreateTaskRequest { Title = "Tarefa", Priority = protoPriority };

        var result = TaskGrpcMapping.ToApplicationRequest(request);

        result.Request!.Priority.Should().Be(prioridadeEsperada);
    }

    [Fact] // description ausente (HasDescription=false) vira null, não string vazia.
    public void ToApplicationRequest_SemDescription_MapeiaParaNull()
    {
        var request = new ProtoCreateTaskRequest { Title = "Tarefa" };

        var result = TaskGrpcMapping.ToApplicationRequest(request);

        result.Request!.Description.Should().BeNull();
    }

    [Fact]
    public void ToApplicationRequest_ComDescription_PreservaOValor()
    {
        var request = new ProtoCreateTaskRequest { Title = "Tarefa", Description = "Detalhe" };

        var result = TaskGrpcMapping.ToApplicationRequest(request);

        result.Request!.Description.Should().Be("Detalhe");
    }

    [Fact] // due_date ausente (HasDueDate=false) vira null.
    public void ToApplicationRequest_SemDueDate_MapeiaParaNull()
    {
        var request = new ProtoCreateTaskRequest { Title = "Tarefa" };

        var result = TaskGrpcMapping.ToApplicationRequest(request);

        result.IsValid.Should().BeTrue();
        result.Request!.DueDate.Should().BeNull();
    }

    [Fact]
    public void ToApplicationRequest_ComDueDateValido_MapeiaParaODateOnlyCorrespondente()
    {
        var request = new ProtoCreateTaskRequest { Title = "Tarefa", DueDate = "2026-08-21" };

        var result = TaskGrpcMapping.ToApplicationRequest(request);

        result.IsValid.Should().BeTrue();
        result.Request!.DueDate.Should().Be(new DateOnly(2026, 8, 21));
    }

    [Theory] // due_date fora do formato yyyy-MM-dd é erro de validação, nunca exceção.
    [InlineData("31/12/2026")]
    [InlineData("2026-13-01")]
    [InlineData("não é uma data")]
    public void ToApplicationRequest_ComDueDateForaDoFormato_DevolveErroDeValidacaoNoCampoDueDate(string dueDate)
    {
        var request = new ProtoCreateTaskRequest { Title = "Tarefa", DueDate = dueDate };

        var result = TaskGrpcMapping.ToApplicationRequest(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainKey(TaskGrpcMapping.DueDateFieldName);
    }

    [Fact]
    public void ToTaskReply_ComTarefaCriada_EspelhaOTaskResponse()
    {
        var task = TodoTask.Create(
            Guid.NewGuid(), "Tarefa", "Descrição", TaskPriority.High, new DateOnly(2026, 1, 1), TimeProvider.System).Value;
        var response = task.ToResponse(new DateOnly(2026, 1, 2));

        var reply = TaskGrpcMapping.ToTaskReply(response);

        reply.Id.Should().Be(response.Id.ToString());
        reply.Title.Should().Be(response.Title);
        reply.Description.Should().Be(response.Description);
        reply.Priority.Should().Be(ProtoTaskPriority.High);
        reply.Status.Should().Be(TodoList.Contracts.Tasks.V1.TaskStatus.Pending);
        reply.DueDate.Should().Be("2026-01-01");
        reply.IsOverdue.Should().BeTrue();
        reply.CompletedAt.Should().BeNull();
    }
}
