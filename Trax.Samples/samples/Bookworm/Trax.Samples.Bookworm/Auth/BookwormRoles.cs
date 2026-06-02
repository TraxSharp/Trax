namespace Trax.Samples.Bookworm.Auth;

/// <summary>Role names used by Bookworm's <c>[TraxAuthorize]</c> gates.</summary>
public static class BookwormRoles
{
    /// <summary>A library member who can borrow and return books.</summary>
    public const string Member = "Member";

    /// <summary>A librarian with administrative access.</summary>
    public const string Librarian = "Librarian";
}
