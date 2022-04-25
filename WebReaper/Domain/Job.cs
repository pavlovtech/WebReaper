using System.Collections.Immutable;
namespace WebReaper.Domain;

public record Job(
    SchemaElement[] schema,
    string BaseUrl,
    string Url,
    ImmutableQueue<LinkPathSelector> LinkPathSelectors,
    int DepthLevel = 0) {
        public PageCategory PageCategory { get {
            if(!LinkPathSelectors.Any()) {
                return PageCategory.TargetPage;
            }

            var currentSelector = LinkPathSelectors.Peek();

            if(LinkPathSelectors.Count() == 1 &&
                currentSelector.PaginationSelector != null) {
                return PageCategory.PageWithPagination;
            }

            return PageCategory.TransitPage;
        }}

        public int Priority => PageCategory switch
        {
            PageCategory.TargetPage => -int.MaxValue,
            PageCategory.PageWithPagination => -DepthLevel * (int)PageCategory.PageWithPagination,
            PageCategory.TransitPage => -DepthLevel * (int)PageCategory.TransitPage,
            PageCategory.Unknown => 0,
            _ => int.MaxValue
        };
    };
