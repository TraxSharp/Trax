// What the example on sdk-reference/train-methods/gate.md names but does not declare.
using Trax.Core.Decisions;
using Trax.Core.Junction;

public record Message(string From, string Body);

public record Delivery(string Status);

[Asks("Does the message threaten someone?")]
public sealed class ContainsThreat;

public class Block : Junction<Message, Delivery>
{
    public override Task<Delivery> Run(Message input) => Task.FromResult(new Delivery("blocked"));
}

public class Deliver : Junction<Message, Delivery>
{
    public override Task<Delivery> Run(Message input) => Task.FromResult(new Delivery("delivered"));
}

public class HoldForReview : Junction<Message, Delivery>
{
    public override Task<Delivery> Run(Message input) => Task.FromResult(new Delivery("held"));
}
