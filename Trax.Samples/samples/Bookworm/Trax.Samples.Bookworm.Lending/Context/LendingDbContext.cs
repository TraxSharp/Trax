using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DomainContext;
using Trax.Samples.Bookworm.Lending.Models.Loans;
using Trax.Samples.Bookworm.Lending.Models.Members;

namespace Trax.Samples.Bookworm.Lending.Context;

/// <summary>
/// The lending domain's data context. Owns the <c>lending</c> schema. Holds no reference to the
/// catalog domain: a loan's <see cref="Loan.BookId"/> is a plain integer, and the relationship to a
/// catalog book is resolved at the GraphQL layer by the cross-schema edge project.
/// </summary>
/// <remarks>
/// Members and loans are per-member data, so both carry an owner-scope query filter that reads
/// <see cref="ILendingCaller"/>: a member reads their own member row and loans, a librarian reads
/// every row. <c>[TraxAuthorize]</c> on the models gates the types; these filters gate the rows,
/// and Trax's authorization never sees them. The filters apply to every query through this context,
/// the GraphQL query models and the trains alike.
/// </remarks>
public class LendingDbContext : DomainDataContext<LendingDbContext>, ILendingDbContext
{
    private readonly ILendingCaller _caller;

    /// <summary>
    /// The constructor DI uses: the caller comes from the request. Marked so the container and
    /// EF's context factory choose it over the options-only constructor below.
    /// </summary>
    [ActivatorUtilitiesConstructor]
    public LendingDbContext(DbContextOptions<LendingDbContext> options, ILendingCaller caller)
        : base(options) => _caller = caller;

    /// <summary>
    /// For tools that build the context offline (the architecture guards, design-time tooling).
    /// The caller is nobody, so every owner-scoped row is filtered out.
    /// </summary>
    public LendingDbContext(DbContextOptions<LendingDbContext> options)
        : this(options, NoLendingCaller.Instance) { }

    public DbSet<Member> Members => Set<Member>();
    public DbSet<Loan> Loans => Set<Loan>();

    protected override string Schema => LendingSchema.Name;

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Member>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.Name).IsRequired();
            entity.HasIndex(e => e.Email).IsUnique();
            entity.HasIndex(e => e.PrincipalId).IsUnique();

            // A member reads their own row; a librarian reads every row.
            entity.HasQueryFilter(m =>
                _caller.IsLibrarian
                || (_caller.PrincipalId != null && m.PrincipalId == _caller.PrincipalId)
            );
        });

        modelBuilder.Entity<Loan>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            // Same-schema FK to Member, configured without a navigation property so the loan stays a
            // flat scalar shape in GraphQL.
            entity
                .HasOne<Member>()
                .WithMany()
                .HasForeignKey(e => e.MemberId)
                .OnDelete(DeleteBehavior.Cascade);

            // At most one open loan per book. The borrow train checks first for a readable error;
            // this index is what stops two concurrent borrows of one book from both succeeding.
            entity.HasIndex(e => e.BookId).IsUnique().HasFilter("returned_at IS NULL");

            // A member reads the loans of their own member row; a librarian reads every loan.
            entity.HasQueryFilter(l =>
                _caller.IsLibrarian
                || Set<Member>()
                    .Any(m =>
                        m.Id == l.MemberId
                        && _caller.PrincipalId != null
                        && m.PrincipalId == _caller.PrincipalId
                    )
            );
        });
    }
}
