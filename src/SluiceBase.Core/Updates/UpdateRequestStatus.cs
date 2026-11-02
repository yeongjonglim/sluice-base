namespace SluiceBase.Core.Updates;

public enum UpdateRequestStatus
{
    Pending,
    Approved,
    // Claimed by an executor whose SQL is running; nothing else can change the request until it is recorded.
    Executing,
    Rejected,
    Cancelled,
    Executed
}