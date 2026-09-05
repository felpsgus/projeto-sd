using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using TodoList.Tasks.Application.Errors;
using TodoList.Tasks.Application.Identity;
using TodoList.Tasks.Application.Persistence;
using TodoList.Tasks.Application.Security;
using TodoList.Tasks.Application.Tasks;
using TodoList.Tasks.Domain.Tasks;
using Xunit;

namespace TodoList.Tasks.UnitTests.Tasks;

/// <summary>
/// <see cref="CreateTaskHandler"/> (BE-17, BE-28) com
/// <see cref="ITodoTaskRepository"/>, <see cref="IUnitOfWork"/> e
/// <see cref="IIdentityGateway"/> substituídos (NSubstitute) — a ordem
/// dono → limite → persistência (CA-23 de BE-17; CA-02, CA-03 de BE-28) e os
/// quatro desfechos da tabela de BE-28. Por padrão, o gateway devolve
/// <c>Exists=true, Active=true</c> (regra de "Testes obrigatórios" de BE-17).
/// </summary>
public class CreateTaskHandlerTests
{
    private static readonly Guid _ownerId = Guid.NewGuid();

    private readonly ITodoTaskRepository _repository = Substitute.For<ITodoTaskRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IIdentityGateway _identityGateway = Substitute.For<IIdentityGateway>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IClientDate _clientDate = Substitute.For<IClientDate>();
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

    public CreateTaskHandlerTests()
    {
        _currentUser.Id.Returns(_ownerId);
        _clientDate.Today.Returns(new DateOnly(2026, 1, 1));
        _identityGateway.ValidateUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new UserValidation(Exists: true, Active: true, DisplayName: "Ada Lovelace"));
        _repository.CountActiveByOwnerAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(0);
    }

    [Fact] // CA-03, CA-04 de BE-17
    public async Task HandleAsync_RequestValido_CriaTarefaPendingComPriorityMediumQuandoOmitida()
    {
        var handler = CreateHandler();
        var request = new CreateTaskRequest("Comprar leite", null, null, null);

        var result = await handler.HandleAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(TodoTaskStatus.Pending);
        result.Value.CompletedAt.Should().BeNull();
        result.Value.Priority.Should().Be(TaskPriority.Medium);
    }

    [Fact] // CA-06 de BE-17 — OwnerId vem de ICurrentUser.Id
    public async Task HandleAsync_RequestValido_PersisteTarefaComOwnerIdDoCurrentUser()
    {
        var handler = CreateHandler();

        await handler.HandleAsync(new CreateTaskRequest("Tarefa", null, null, null), CancellationToken.None);

        _repository.Received(1).Add(Arg.Is<TodoTask>(task => task.OwnerId == _ownerId));
    }

    [Fact] // CA-23 de BE-17; CA-02, CA-03 de BE-28
    public async Task HandleAsync_ChamaValidateUserAsyncUmaVez_AntesDoLimiteEAntesDoSaveChanges()
    {
        var handler = CreateHandler();

        await handler.HandleAsync(new CreateTaskRequest("Tarefa", null, null, null), CancellationToken.None);

        await _identityGateway.Received(1).ValidateUserAsync(_ownerId, Arg.Any<CancellationToken>());

        Received.InOrder(() =>
        {
            _identityGateway.ValidateUserAsync(_ownerId, Arg.Any<CancellationToken>());
            _repository.CountActiveByOwnerAsync(_ownerId, Arg.Any<CancellationToken>());
            _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact] // BE-28, tabela — Exists=false
    public async Task HandleAsync_DonoInexistenteNoIdentity_RetornaOwnerNotFoundSemPersistir()
    {
        _identityGateway.ValidateUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new UserValidation(Exists: false, Active: false, DisplayName: string.Empty));
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new CreateTaskRequest("Tarefa", null, null, null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TaskErrors.OwnerNotFound);
        AssertNadaFoiPersistido();
    }

    [Fact] // BE-28, tabela — Exists=true, Active=false
    public async Task HandleAsync_DonoInativoNoIdentity_RetornaOwnerInactiveSemPersistir()
    {
        _identityGateway.ValidateUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new UserValidation(Exists: true, Active: false, DisplayName: "Charles Babbage"));
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new CreateTaskRequest("Tarefa", null, null, null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TaskErrors.OwnerInactive);
        AssertNadaFoiPersistido();
    }

    [Fact] // BE-28, tabela — falha de transporte, D-28 fail-closed
    public async Task HandleAsync_IdentityIndisponivel_RetornaIdentityUnavailableSemPersistir()
    {
        _identityGateway.ValidateUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns<UserValidation>(_ => throw new IdentityUnavailableException("Identity fora do ar"));
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new CreateTaskRequest("Tarefa", null, null, null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TaskErrors.IdentityUnavailable);
        AssertNadaFoiPersistido();
    }

    [Fact] // CA-14 — no limite, a criação que o atinge sucede
    public async Task HandleAsync_ContagemAbaixoDoLimite_Sucede()
    {
        _repository.CountActiveByOwnerAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(499);
        var handler = CreateHandlerComLimite(500);

        var result = await handler.HandleAsync(new CreateTaskRequest("Tarefa", null, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact] // CA-14 — no limite, a criação seguinte falha com 409
    public async Task HandleAsync_ContagemNoLimite_RetornaActiveLimitReachedSemPersistir()
    {
        _repository.CountActiveByOwnerAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(500);
        var handler = CreateHandlerComLimite(500);

        var result = await handler.HandleAsync(new CreateTaskRequest("Tarefa", null, null, null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("task.active_limit_reached");
        result.Error.Message.Should().Contain("500");
        AssertNadaFoiPersistido();
    }

    [Fact] // CA-16/CA-17 (na fronteira do handler): a "atividade" é inteiramente definida pelo que CountActiveByOwnerAsync devolve —
           // a composição real (concluídas e soft-deleted não contam) é responsabilidade do repositório real, coberta em
           // TodoList.Tasks.IntegrationTests.Tasks.CreateTaskActiveLimitTests contra um banco de verdade.
    public async Task HandleAsync_ContagemZero_AindaQueHouvesseTarefasConcluidasOuRemovidas_Sucede()
    {
        _repository.CountActiveByOwnerAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(0);
        var handler = CreateHandlerComLimite(500);

        var result = await handler.HandleAsync(new CreateTaskRequest("Tarefa", null, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact] // CA-18 — o limite é por usuário: a contagem vem de CountActiveByOwnerAsync(ownerId), não de um total global
    public async Task HandleAsync_ContagemEhConsultadaParaODonoCorrente_NaoParaOutroUsuario()
    {
        var handler = CreateHandlerComLimite(500);

        await handler.HandleAsync(new CreateTaskRequest("Tarefa", null, null, null), CancellationToken.None);

        await _repository.Received(1).CountActiveByOwnerAsync(_ownerId, Arg.Any<CancellationToken>());
    }

    [Fact] // CA-19 — limite configurável: reduzir para 3 bloqueia na 4ª, sem mudança de código
    public async Task HandleAsync_ComLimiteReduzidoParaTres_BloqueiaQuandoContagemJaEhTres()
    {
        _repository.CountActiveByOwnerAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(3);
        var handler = CreateHandlerComLimite(3);

        var result = await handler.HandleAsync(new CreateTaskRequest("Tarefa", null, null, null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("3");
    }

    [Fact] // CA-20 — limite nulo desativa o bloqueio, mesmo com contagem alta
    public async Task HandleAsync_ComLimiteNulo_NaoBloqueiaMesmoComContagemAlta()
    {
        _repository.CountActiveByOwnerAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(10_000);
        var handler = CreateHandlerComLimite(null);

        var result = await handler.HandleAsync(new CreateTaskRequest("Tarefa", null, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        // Com o limite desativado, a contagem nem precisa ser consultada.
        await _repository.DidNotReceive().CountActiveByOwnerAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    private void AssertNadaFoiPersistido()
    {
        _repository.DidNotReceive().Add(Arg.Any<TodoTask>());
        _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private CreateTaskHandler CreateHandler() => CreateHandlerComLimite(500);

    private CreateTaskHandler CreateHandlerComLimite(int? maxActivePerUser) =>
        new(
            _repository,
            _unitOfWork,
            _identityGateway,
            _currentUser,
            _clientDate,
            Options.Create(new TaskOptions { MaxActivePerUser = maxActivePerUser }),
            _timeProvider,
            NullLogger<CreateTaskHandler>.Instance);
}
