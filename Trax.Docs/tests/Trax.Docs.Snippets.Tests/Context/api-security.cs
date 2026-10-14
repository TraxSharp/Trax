// What the compiled examples on api-security.md take as given: the invoicing trains' interfaces,
// inputs, outputs and junctions.
using Trax.Core.Junction;
using Trax.Effect.Services.ServiceTrain;

public record IssueInvoiceInput(string Customer, decimal Amount);

public record IssueInvoiceOutput(long InvoiceId);

public interface IIssueInvoiceTrain : IServiceTrain<IssueInvoiceInput, IssueInvoiceOutput>;

public class IssueInvoiceJunction : Junction<IssueInvoiceInput, IssueInvoiceOutput>
{
    public override Task<IssueInvoiceOutput> Run(IssueInvoiceInput input) =>
        Task.FromResult(new IssueInvoiceOutput(1));
}

public record ListInvoicesInput;

public record InvoiceSummary(long Id, string Customer, decimal Amount);

public record ListInvoicesOutput(IReadOnlyList<InvoiceSummary> Invoices);

public interface IListInvoicesTrain : IServiceTrain<ListInvoicesInput, ListInvoicesOutput>;

public class ListInvoicesJunction : Junction<ListInvoicesInput, ListInvoicesOutput>
{
    public override Task<ListInvoicesOutput> Run(ListInvoicesInput input) =>
        Task.FromResult(new ListInvoicesOutput([]));
}
