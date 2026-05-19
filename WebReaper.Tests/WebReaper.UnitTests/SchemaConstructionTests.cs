using WebReaper.Domain.Parsing;

namespace WebReaper.UnitTests;

// ADR-0028: the Schema grammar's enforcement lives at the construction
// site — Schema.Add validates every child, and Schema.ListOf bundles
// the list-of-objects shape so the IsList + Selector + Children triple
// is never authored apart. Tests pin every Add-site rule and the
// factory's contract at the construction interface (no parser, no fold,
// no document).
public class SchemaConstructionTests
{
    // ---- Add-site rules ----

    [Fact]
    public void Add_rejects_null_element()
    {
        var schema = new Schema();
        Assert.Throws<ArgumentNullException>(() => schema.Add(null!));
    }

    [Fact]
    public void Add_rejects_a_leaf_with_empty_field()
    {
        // The default ctor leaves Field as null on a SchemaElement.
        var orphan = new SchemaElement { Selector = ".x" };
        Assert.Throws<ArgumentException>(() => new Schema { orphan });
    }

    [Fact]
    public void Add_rejects_a_leaf_with_empty_selector()
    {
        var ex = Assert.Throws<ArgumentException>(() => new Schema
        {
            new SchemaElement("name", selector: "")
        });
        Assert.Contains("Leaf 'name'", ex.Message);
    }

    [Fact]
    public void Add_rejects_a_leaf_list_with_empty_selector()
    {
        var ex = Assert.Throws<ArgumentException>(() => new Schema
        {
            new SchemaElement("xs", selector: "") { IsList = true }
        });
        Assert.Contains("Leaf 'xs'", ex.Message);
    }

    [Fact]
    public void Add_rejects_a_list_container_with_empty_selector()
    {
        var ex = Assert.Throws<ArgumentException>(() => new Schema
        {
            new Schema("listings")
            {
                IsList = true,
                Children = { new SchemaElement("name", ".n") }
            }
        });
        Assert.Contains("List container 'listings'", ex.Message);
    }

    [Fact]
    public void Add_allows_a_nested_non_list_object_with_no_selector()
    {
        // A non-list nested Schema uses the parent's scope — the fold
        // never reads its own Selector. Schema.Add exempts it.
        var schema = new Schema
        {
            new Schema("post")
            {
                Children =
                {
                    new SchemaElement("title", ".t"),
                    new SchemaElement("views", ".v", DataType.Integer)
                }
            }
        };

        Assert.Single(schema.Children);
        var post = Assert.IsType<Schema>(schema.Children[0]);
        Assert.Equal("post", post.Field);
        Assert.Null(post.Selector);
        Assert.False(post.IsList);
        Assert.Equal(2, post.Children.Count);
    }

    [Fact]
    public void Add_accepts_a_well_formed_leaf()
    {
        var schema = new Schema
        {
            new SchemaElement("name", ".name"),
            new SchemaElement("price", ".price", DataType.Integer),
            new SchemaElement("href", ".link", "href"),
        };

        Assert.Equal(3, schema.Children.Count);
    }

    // ---- Schema.ListOf factory ----

    [Fact]
    public void ListOf_rejects_empty_field()
    {
        Assert.Throws<ArgumentException>(() =>
            Schema.ListOf(field: "", selector: ".x"));
        Assert.Throws<ArgumentException>(() =>
            Schema.ListOf(field: "   ", selector: ".x"));
    }

    [Fact]
    public void ListOf_rejects_empty_selector()
    {
        Assert.Throws<ArgumentException>(() =>
            Schema.ListOf(field: "items", selector: ""));
        Assert.Throws<ArgumentException>(() =>
            Schema.ListOf(field: "items", selector: "   "));
    }

    [Fact]
    public void ListOf_builds_a_list_container_with_validated_children()
    {
        var listings = Schema.ListOf("listings", ".card",
            new SchemaElement("name", ".name"),
            new SchemaElement("price", ".price", DataType.Integer));

        Assert.Equal("listings", listings.Field);
        Assert.Equal(".card", listings.Selector);
        Assert.True(listings.IsList);
        Assert.Equal(2, listings.Children.Count);
        Assert.Equal("name", listings.Children[0].Field);
        Assert.Equal(".name", listings.Children[0].Selector);
    }

    [Fact]
    public void ListOf_validates_children_through_Add()
    {
        // Any bad child fails the factory call (Add runs per child).
        Assert.Throws<ArgumentException>(() =>
            Schema.ListOf("listings", ".card",
                new SchemaElement("bad", selector: "")));
    }

    [Fact]
    public void ListOf_nests_inside_a_root_schema_through_Add()
    {
        // The factory's result composes naturally with the existing
        // collection-initialiser shape — the Schema.Add path validates
        // the list container's own Selector (non-empty, set by ListOf).
        var root = new Schema
        {
            Schema.ListOf("listings", ".card",
                new SchemaElement("name", ".name"))
        };

        Assert.Single(root.Children);
        var inner = Assert.IsType<Schema>(root.Children[0]);
        Assert.True(inner.IsList);
        Assert.Equal(".card", inner.Selector);
    }
}
