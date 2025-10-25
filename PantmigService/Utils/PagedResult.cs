using System;
using System.Collections.Generic;
using System.Linq;

namespace PantmigService.Utils
{
 // Generic, reusable pagination result
 public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize)
 {
 public int TotalPages => PageSize <=0 ?0 : (int)Math.Ceiling((double)Total / PageSize);
 public bool HasPrevious => Page >1;
 public bool HasNext => Page < TotalPages;

 // Map items into another type while preserving pagination metadata
 public PagedResult<TOut> Map<TOut>(Func<T, TOut> map)
 => new([
 .. Items.Select(map)
 ], Total, Page, PageSize);
 }
}
