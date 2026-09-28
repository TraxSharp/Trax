namespace Trax.Mediator.Tests.Meta.Tests;

/// <summary>
/// The Mediator hands a queue entry's subject key to <c>WorkQueue.Create</c> through
/// <c>CreateWorkQueue.SubjectKey</c> and never assigns it afterwards, so the key rules
/// <c>Create</c> enforces apply to every entry the Mediator writes.
///
/// <para>Enforces <c>Trax.Docs/adr/0019-queued-work-for-one-subject-runs-one-at-a-time.md</c>.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0019-queued-work-for-one-subject-runs-one-at-a-time.md")]
[TestFixture]
public class SubjectKeyGoesThroughCreateTests
{
    private static readonly Regex Assignment = new(@"\.SubjectKey\s*=(?!=)", RegexOptions.Compiled);

    [Test]
    public void No_source_assigns_SubjectKey_to_an_entry_after_it_was_created()
    {
        var offenders = SourceFiles
            .CSharp("src")
            .Where(file => Assignment.IsMatch(File.ReadAllText(file)))
            .Select(RepoRoot.Relative)
            .ToList();

        offenders
            .Should()
            .BeEmpty(
                "a key assigned after WorkQueue.Create skips the rules Create enforces; pass it "
                    + "as CreateWorkQueue.SubjectKey instead "
                    + "(Trax.Docs/adr/0019-queued-work-for-one-subject-runs-one-at-a-time.md)"
            );
    }
}
