using FluentAssertions;
using NUnit.Framework;
using Trax.Samples.JobHunt.Providers.Scraper;

namespace Trax.Samples.JobHunt.Tests.UnitTests.Providers.Scraper;

[TestFixture]
public class GenericHtmlScraperTests
{
    [Test]
    public void ParseHtml_JsonLdJobPosting_ExtractsTitleCompanyDescription()
    {
        var html = """
            <html><head>
            <script type="application/ld+json">
            {
              "@context": "https://schema.org",
              "@type": "JobPosting",
              "title": "Senior Engineer",
              "hiringOrganization": { "name": "Acme Corp" },
              "description": "Build <b>distributed</b> systems"
            }
            </script>
            </head><body></body></html>
            """;

        var result = GenericHtmlScraper.ParseHtml(html);

        result.Title.Should().Be("Senior Engineer");
        result.Company.Should().Be("Acme Corp");
        result.Description.Should().Be("Build distributed systems");
    }

    [Test]
    public void ParseHtml_JsonLdStringOrg_ExtractsCompany()
    {
        var html = """
            <html><head>
            <script type="application/ld+json">
            {
              "@type": "JobPosting",
              "title": "Dev",
              "hiringOrganization": "SimpleOrg",
              "description": "Work"
            }
            </script>
            </head><body></body></html>
            """;

        var result = GenericHtmlScraper.ParseHtml(html);

        result.Company.Should().Be("SimpleOrg");
    }

    [Test]
    public void ParseHtml_OpenGraphFallback_ExtractsTitleAndDescription()
    {
        var html = """
            <html><head>
            <meta property="og:title" content="Staff Engineer at MegaCorp" />
            <meta property="og:description" content="Lead distributed systems" />
            <meta property="og:site_name" content="MegaCorp Careers" />
            </head><body></body></html>
            """;

        var result = GenericHtmlScraper.ParseHtml(html);

        result.Title.Should().Be("Staff Engineer at MegaCorp");
        result.Company.Should().Be("MegaCorp Careers");
        result.Description.Should().Be("Lead distributed systems");
    }

    [Test]
    public void ParseHtml_NoStructuredData_FallsBackToTitleAndParagraphs()
    {
        var html = """
            <html><head><title>Cool Job - Apply Now</title></head>
            <body>
            <p>This is a really interesting opportunity to join our growing team and work on exciting challenges.</p>
            </body></html>
            """;

        var result = GenericHtmlScraper.ParseHtml(html);

        result.Title.Should().Be("Cool Job - Apply Now");
        result.Description.Should().Contain("interesting opportunity");
    }

    [Test]
    public void ParseHtml_MalformedJsonLd_FallsBackToOg()
    {
        var html = """
            <html><head>
            <script type="application/ld+json">{ NOT VALID JSON }</script>
            <meta property="og:title" content="Fallback Title" />
            </head><body></body></html>
            """;

        var result = GenericHtmlScraper.ParseHtml(html);

        result.Title.Should().Be("Fallback Title");
    }

    [Test]
    public void ParseHtml_NonJobPostingJsonLd_FallsBackToOg()
    {
        var html = """
            <html><head>
            <script type="application/ld+json">
            { "@type": "Organization", "name": "NotAJob" }
            </script>
            <meta property="og:title" content="Real Title" />
            </head><body></body></html>
            """;

        var result = GenericHtmlScraper.ParseHtml(html);

        result.Title.Should().Be("Real Title");
    }

    [Test]
    public void ParseHtml_EmptyHtml_ReturnsNulls()
    {
        var result = GenericHtmlScraper.ParseHtml("<html><head></head><body></body></html>");

        result.Title.Should().BeNull();
        result.Company.Should().BeNull();
        result.Description.Should().BeNull();
    }

    [Test]
    public void ParseHtml_HtmlTagsInDescription_Stripped()
    {
        var html = """
            <html><head>
            <script type="application/ld+json">
            {
              "@type": "JobPosting",
              "title": "Dev",
              "description": "Good job with <b>bold</b> and <em>italic</em> text"
            }
            </script>
            </head><body></body></html>
            """;

        var result = GenericHtmlScraper.ParseHtml(html);

        result.Description.Should().NotContain("<b>");
        result.Description.Should().NotContain("<em>");
        result.Description.Should().Contain("Good job with bold and italic text");
    }
}
