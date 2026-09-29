using System.ComponentModel;

namespace Trax.Dashboard.Models;

/// <summary>
/// One page of rows for a server-side <c>TraxDataGrid</c>, plus the total the pager needs.
/// Returned by <see cref="Utilities.DataGridQueryHelper.LoadPageAsync{T}"/>. Public only because
/// <c>TraxDataGrid.ServerLoadData</c> exposes it; not intended for use outside this package.
/// </summary>
/// <typeparam name="T">The grid's row type.</typeparam>
/// <param name="Items">The rows of the requested page, after filtering, sorting, skip and take.</param>
/// <param name="TotalCount">The number of rows matching the filter before paging.</param>
[EditorBrowsable(EditorBrowsableState.Never)]
public record ServerDataResult<T>(IEnumerable<T> Items, int TotalCount);
