using SluiceBase.Core.Common;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;
using Stateless;

namespace SluiceBase.Core.Updates;

public sealed class UpdateRequest
{
#pragma warning disable CS8618
    private UpdateRequest()
    {
    }
#pragma warning restore CS8618

    public UpdateRequestId Id { get; private set; }
    public DatabaseId? DatabaseId { get; private set; }
    public UserId? SubmitterId { get; private set; }
    public string SqlText { get; private set; }
    public string Reason { get; private set; }
    public UpdateRequestStatus Status { get; private set; }
    public UserId? ReviewerId { get; private set; }
    public string? ReviewNote { get; private set; }

    public UpdateRequestId? SourceRequestId { get; private set; }
    public UserId? CancelledById { get; private set; }
    public string? CancelNote { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

    public UserId? ExecutorId { get; private set; }
    public DateTimeOffset SubmittedAt { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }
    public DateTimeOffset? ExecutedAt { get; private set; }
    public bool? ExecSuccess { get; private set; }
    public int? ExecDurationMs { get; private set; }
    public int? ExecAffectedRows { get; private set; }
    public string? ExecError { get; private set; }

    // Linked by EF relationship
    public Database? Database { get; private set; }
    public User? Submitter { get; private set; }
    public User? Reviewer { get; private set; }
    public User? Executor { get; private set; }
    public User? CancelledBy { get; private set; }

    // Trigger names are an implementation detail — hidden from callers.
    private enum Trigger
    {
        Approve,
        Reject,
        Cancel,
        Execute
    }

    private StateMachine<UpdateRequestStatus, Trigger> StateMachine => BuildMachine();

    private abstract record BaseTriggerRequest(Actioned Actioned); // Not used anywhere yet

    private sealed record TriggerRequest(Actioned Actioned, string Note) : BaseTriggerRequest(Actioned);

    private sealed record ExecuteTriggerRequest(
        Actioned Actioned,
        bool Success,
        int DurationMs,
        int? AffectedRows,
        string? Error
    ) : BaseTriggerRequest(Actioned);

    // Builds a Stateless machine whose accessor/mutator reads and writes this entity's Status.
    // Each new machine instance reflects current status, so Build() is called per operation.
    private StateMachine<UpdateRequestStatus, Trigger> BuildMachine()
    {
        var machine = new StateMachine<UpdateRequestStatus, Trigger>(
            stateAccessor: () => Status,
            stateMutator: s => Status = s);

        var approveTrigger = machine.SetTriggerParameters<TriggerRequest>(Trigger.Approve);
        var rejectTrigger = machine.SetTriggerParameters<TriggerRequest>(Trigger.Reject);
        var cancelTrigger = machine.SetTriggerParameters<TriggerRequest>(Trigger.Cancel);
        var executeTrigger = machine.SetTriggerParameters<ExecuteTriggerRequest>(Trigger.Execute);

        machine.Configure(UpdateRequestStatus.Pending)
            .PermitIf(approveTrigger, UpdateRequestStatus.Approved)
            .PermitIf(rejectTrigger, UpdateRequestStatus.Rejected)
            .PermitIf(cancelTrigger, UpdateRequestStatus.Cancelled);

        machine.Configure(UpdateRequestStatus.Approved)
            .OnEntryFrom(approveTrigger,
                x =>
                {
                    ReviewerId = x.Actioned.UserId;
                    ReviewNote = x.Note;
                    ReviewedAt = x.Actioned.At;
                })
            .PermitIf(cancelTrigger, UpdateRequestStatus.Cancelled)
            .PermitIf(executeTrigger, UpdateRequestStatus.Executed);

        machine.Configure(UpdateRequestStatus.Rejected)
            .OnEntryFrom(rejectTrigger,
                x =>
                {
                    ReviewerId = x.Actioned.UserId;
                    ReviewNote = x.Note;
                    ReviewedAt = x.Actioned.At;
                });

        machine.Configure(UpdateRequestStatus.Cancelled)
            .OnEntryFrom(cancelTrigger,
                x =>
                {
                    CancelledById = x.Actioned.UserId;
                    CancelNote = x.Note;
                    CancelledAt = x.Actioned.At;
                });

        machine.Configure(UpdateRequestStatus.Executed)
            .OnEntryFrom(executeTrigger,
                x =>
                {
                    ExecutorId = x.Actioned.UserId;
                    ExecutedAt = x.Actioned.At;
                    ExecSuccess = x.Success;
                    ExecDurationMs = x.DurationMs;
                    ExecAffectedRows = x.AffectedRows;
                    ExecError = x.Error;
                });

        return machine;
    }

    public static UpdateRequest Create(
        DatabaseId databaseId,
        string sqlText,
        string reason,
        Actioned by,
        UpdateRequestId? sourceRequestId = null) => new()
    {
        Id = UpdateRequestId.FromNewVersion7Guid(),
        DatabaseId = databaseId,
        SubmitterId = by.UserId,
        SqlText = sqlText,
        Reason = reason,
        Status = UpdateRequestStatus.Pending,
        SubmittedAt = by.At,
        SourceRequestId = sourceRequestId,
    };

    // Fires the machine trigger (throws InvalidOperationException on invalid transition),
    // then sets the supplementary review fields.
    public void Approve(Actioned actioned, string note)
    {
        var triggerWithParameters =
            new StateMachine<UpdateRequestStatus, Trigger>.TriggerWithParameters<TriggerRequest>(
                Trigger.Approve);
        StateMachine.Fire(triggerWithParameters, new TriggerRequest(actioned, note));
    }

    public void Reject(Actioned actioned, string note)
    {
        var triggerWithParameters =
            new StateMachine<UpdateRequestStatus, Trigger>.TriggerWithParameters<TriggerRequest>(
                Trigger.Reject);
        StateMachine.Fire(triggerWithParameters, new TriggerRequest(actioned, note));
    }

    public void Cancel(Actioned actioned, string note)
    {
        var triggerWithParameters =
            new StateMachine<UpdateRequestStatus, Trigger>.TriggerWithParameters<TriggerRequest>(
                Trigger.Cancel);
        StateMachine.Fire(triggerWithParameters, new TriggerRequest(actioned, note));
    }

    // Fires the Execute trigger and records the outcome.
    // The caller is responsible for running the SQL and passing the result here.
    public void RecordExecution(
        Actioned actioned,
        bool success,
        int durationMs,
        int? affectedRows,
        string? error
    )
    {
        var triggerWithParameters =
            new StateMachine<UpdateRequestStatus, Trigger>.TriggerWithParameters<ExecuteTriggerRequest>(
                Trigger.Execute);
        StateMachine.Fire(triggerWithParameters,
            new ExecuteTriggerRequest(actioned,
                success,
                durationMs,
                affectedRows,
                error));
    }

    // Exposed for the execute endpoint to pre-check before running expensive SQL.
    public bool CanExecute() => StateMachine.CanFire(Trigger.Execute);
    // public bool CanExecute(
    //     Actioned actioned,
    //     bool success,
    //     int durationMs,
    //     int? affectedRows,
    //     string? error
    //     )
    // {
    //     var triggerWithParameters =
    //         new StateMachine<UpdateRequestStatus, Trigger>.TriggerWithParameters<ExecuteTriggerRequest>(
    //             Trigger.Execute);
    //     return StateMachine.CanFire(triggerWithParameters,
    //         new ExecuteTriggerRequest(actioned,
    //             success,
    //             durationMs,
    //             affectedRows,
    //             error));
    // }
}