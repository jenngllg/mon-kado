using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Services;

using System.Text;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.UnitTests.Services;

public class MerchantMetadataExtractorTests
{
    private readonly MerchantMetadataExtractor _extractor = new();

    [Theory]
    [InlineData("priceToPay")]
    [InlineData("apex-pricetopay-value")]
    public void Extract_WhenAmazonAccessibleLabelIncludesSavings_ReadsActualOfferWithoutDiscountOrCrossedPrice(string priceClass)
    {
        // Arrange
        var document = CreateAmazonDocument($$"""
            <span id="productTitle">Example product</span>
            <div id="corePriceDisplay_desktop_feature_div">
              <span id="apex-pricetopay-accessibility-label">11,82 € avec 14 % d’économies</span>
              <span class="{{priceClass}}"><span class="a-offscreen"> </span><span aria-hidden="true">
                <span class="a-price-whole">11<span class="a-price-decimal">,</span></span>
                <span class="a-price-fraction">82</span><span class="a-price-symbol">€</span>
              </span></span>
              <span class="a-price a-text-price" data-a-strike="true"><span class="a-offscreen">13,78 €</span></span>
              <span class="savingsPercentage">-14 %</span>
              <span class="pricePerUnit">5,91 € / unité</span>
            </div>
            <aside><span class="{{priceClass}}">1,00 €</span></aside>
            """);

        // Act
        var result = _extractor.Extract(document);

        // Assert
        Assert.Equal(
            11.82m,
            result.Price);
        Assert.False(result.CurrencyUnsupported);
    }

    [Theory]
    [InlineData("<meta itemprop='sku' content='large'>", "", "large", 94.00)]
    [InlineData("", "data-product-sku='large'", "large", 94.00)]
    [InlineData("", "data-product-sku='unknown'", "large", null)]
    [InlineData("<meta itemprop='sku' content='small'>", "data-product-sku='large'", "large", 29.90)]
    public void Extract_WhenMicrodataDeclaresSelectedSku_UsesItsOfferWithoutGuessing(
        string markup,
        string attribute,
        string largerSku,
        double? expected)
    {
        // Arrange
        var document = CreateDocument($$"""
            <main itemscope itemtype='https://schema.org/Product' {{attribute}}>
              {{markup}}<meta itemprop='name' content='Fragrance'>
              <div itemprop='offers' itemscope itemtype='https://schema.org/Offer'>
                <meta itemprop='sku' content='small'><meta itemprop='price' content='29.90'><meta itemprop='priceCurrency' content='EUR'>
              </div>
              <div itemprop='offers' itemscope itemtype='https://schema.org/Offer'>
                <meta itemprop='sku' content='{{largerSku}}'><meta itemprop='price' content='94.00'><meta itemprop='priceCurrency' content='EUR'>
              </div>
            </main>
            """);

        // Act
        var result = _extractor.Extract(document);

        // Assert
        Assert.Equal(
            expected.HasValue ? (decimal?)Convert.ToDecimal(expected.Value) : null,
            result.Price);
    }

    [Fact]
    public void Extract_WhenUrlAndSkuDisagree_PrioritizesExactLinkedVariant()
    {
        // Arrange
        var document = CreateDocument("""
            <script type='application/ld+json'>{"@type":"Product","sku":"small","offers":[
              {"sku":"small","url":"/different","price":"29.90","priceCurrency":"EUR"},
              {"sku":"large","url":"/product","price":"97.30","priceCurrency":"EUR"}]}</script>
            """);

        // Act
        var result = _extractor.Extract(document);

        // Assert
        Assert.Equal(
            97.30m,
            result.Price);
    }

    [Fact]
    public void Extract_WhenSelectedSkuHasConflictingOffers_DoesNotChoosePrice()
    {
        // Arrange
        var document = CreateDocument("""
            <script type='application/ld+json'>{"@type":"Product","sku":"selected","offers":[
              {"sku":"selected","price":"29.90","priceCurrency":"EUR"},
              {"sku":"selected","price":"97.30","priceCurrency":"EUR"}]}</script>
            """);

        // Act
        var result = _extractor.Extract(document);

        // Assert
        Assert.Null(result.Price);
    }

    [Theory]
    [InlineData("https://schema.org/", "<meta itemprop='name' content='Main gift'>", "<meta itemprop='image' content='/main.jpg'>")]
    [InlineData("http://schema.org/", "<span itemprop='name'> Main   gift </span>", "<img itemprop='image' src='/main.jpg'>")]
    [InlineData("https://schema.org/", "<a itemprop='name' href='Main gift'>Ignored</a>", "<a itemprop='image' href='/main.jpg'>Ignored</a>")]
    public void Extract_WhenMicrodataOffersDescribeVariants_SelectsCurrentPageAndExcludesNestedProperties(
        string schema,
        string name,
        string image)
    {
        // Arrange
        var document = CreateDocument($$"""
            <main itemscope itemtype='{{schema}}Product'>
              <span itemprop='brand' itemscope itemtype='{{schema}}Brand'><span itemprop='name'>Brand</span></span>
              {{name}}{{image}}
              <div itemprop='review' itemscope><span itemprop='name'>A review</span></div>
              <div itemprop='offers' itemscope itemtype='{{schema}}Offer'>
                <meta itemprop='price' content='29.90'><meta itemprop='priceCurrency' content='EUR'>
                <meta itemprop='url' content='/different-variant'><span itemprop='name'>Small format</span>
              </div>
              <div itemprop='offers' itemscope itemtype='{{schema}}Offer'>
                <meta itemprop='price' content='97.30'><meta itemprop='priceCurrency' content='EUR'>
                <link itemprop='url' href='/product'>
                <span itemprop='deliveryLeadTime' itemscope><meta itemprop='price' content='1'></span>
              </div>
            </main>
            """);

        // Act
        var result = _extractor.Extract(document);

        // Assert
        Assert.Equal(
            "Main gift",
            result.Name);
        Assert.Equal(
            97.30m,
            result.Price);
        Assert.Equal(
            "https://example.com/main.jpg",
            result.ImageUrl?.AbsoluteUri);
        Assert.False(result.CurrencyUnsupported);
    }

    [Theory]
    [InlineData("<meta itemprop='name' content='One'><meta itemprop='name' content='Two'>")]
    [InlineData("<meta itemprop='name' content='   '>")]
    [InlineData("<div itemscope><meta itemprop='name' content='Nested'></div>")]
    [InlineData("<meta itemprop='name' itemscope content='Not a scalar'>")]
    public void Extract_WhenMicrodataScalarIsMissingOrConflicting_DoesNotGuess(string markup)
    {
        // Arrange
        var document = CreateDocument($$"""
            <main itemscope itemtype='https://schema.org/Product'>{{markup}}</main>
            """);

        // Act
        var result = _extractor.Extract(document);

        // Assert
        Assert.Null(result.Name);
        Assert.Null(result.Price);
        Assert.Null(result.ImageUrl);
    }

    [Fact]
    public void Extract_WhenJsonLdProductExists_DoesNotMergeMicrodataRecommendation()
    {
        // Arrange
        var document = CreateDocument("""
            <script type='application/ld+json'>{"@type":"Product","name":"Main product"}</script>
            <div itemscope itemtype='https://schema.org/Product'><meta itemprop='name' content='Recommendation'></div>
            """);

        // Act
        var result = _extractor.Extract(document);

        // Assert
        Assert.Equal(
            "Main product",
            result.Name);
    }

    [Theory]
    [InlineData("/product", "97.30", "EUR", 97.30)]
    [InlineData("/different", "97.30", "EUR", null)]
    [InlineData("/product?sku=another", "97.30", "EUR", null)]
    [InlineData("/product", "97.30", "USD", null)]
    [InlineData("/product", "97.30", "", null)]
    public void Extract_WhenOfferUrlsIdentifyCurrentVariant_UsesOnlyItsExactEuroPrice(
        string offerUrl,
        string price,
        string currency,
        double? expected)
    {
        // Arrange
        var document = CreateDocument($$"""
            <script type='application/ld+json'>{"@type":"Product","offers":[
              {"url":"/different-small","price":"29.90","priceCurrency":"EUR"},
              {"url":"{{offerUrl}}","price":"{{price}}","priceCurrency":"{{currency}}"}]}</script>
            """);

        // Act
        var result = _extractor.Extract(document);

        // Assert
        Assert.Equal(
            expected.HasValue ? (decimal?)Convert.ToDecimal(expected.Value) : null,
            result.Price);
    }

    [Theory]
    [InlineData("fnac.com")]
    [InlineData("www.fnac.com")]
    public void Extract_WhenModernFnacMarkupIsPresent_UsesMainProductAndPrimaryPrice(string host)
    {
        // Arrange
        var document = new ImportDocument
        {
            Url = new Uri($"https://{host}/product"),
            Content = Encoding.UTF8.GetBytes("""
                <h1 data-automation-id='pdp-productInformation-title'>Headphones</h1>
                <div aria-label='Product images'>
                  <button aria-label='Open image 1 in fullscreen'><img alt='Headphones' src='/main.jpg'></button>
                  <button aria-label='Open image 2 in fullscreen'><img alt='Headphones' src='/second.jpg'></button>
                </div>
                <img alt='Recommendation' src='/other.jpg'>
                <div data-automation-id='pdp-buyBox-desktop'><div><div class='PricingUI-module__container'>
                  <p class='PricingUI__pricingLabelMain'>279,99&nbsp;€</p>
                  <p class='PricingUI__pricingLabelSecondary'>299,99 €</p>
                </div><div class='PricingUI-module__container'><p class='PricingUI__pricingLabelMain'>265,99 € avec carte Fnac+</p></div></div></div>
                <div data-automation-id='pdp-buyBox-mobile'><div><div class='PricingUI-module__container'><p class='PricingUI__pricingLabelMain'>279,99 €</p></div></div></div>
                <aside><p class='PricingUI__pricingLabelMain'>1,00 €</p></aside>
                """),
            MediaType = "text/html"
        };

        // Act
        var result = _extractor.Extract(document);

        // Assert
        Assert.Equal(
            "Headphones",
            result.Name);
        Assert.Equal(
            279.99m,
            result.Price);
        Assert.Equal(
            $"https://{host}/main.jpg",
            result.ImageUrl?.AbsoluteUri);
    }

    [Theory]
    [InlineData("<h1 data-automation-id='pdp-productInformation-title'>One</h1><h1 data-automation-id='pdp-productInformation-title'>Two</h1>")]
    [InlineData("<title>FNAC maintenance</title>")]
    public void Extract_WhenModernFnacIdentityIsAmbiguous_RefusesStorefrontData(string markup)
    {
        // Arrange
        var document = new ImportDocument
        {
            Url = new Uri("https://www.fnac.com/product"),
            Content = Encoding.UTF8.GetBytes(markup),
            MediaType = "text/html"
        };

        // Act
        var result = _extractor.Extract(document);

        // Assert
        Assert.Null(result.Name);
        Assert.Null(result.Price);
        Assert.Null(result.ImageUrl);
    }

    [Theory]
    [InlineData("279,99 €", "299,99 €", false)]
    [InlineData("279.99 USD", "279.99 USD", true)]
    [InlineData("from 279,99 €", "from 279,99 €", false)]
    public void Extract_WhenModernFnacPriceCannotBeChosen_RefusesGuess(
        string desktop,
        string mobile,
        bool unsupported)
    {
        // Arrange
        var document = new ImportDocument
        {
            Url = new Uri("https://www.fnac.com/product"),
            Content = Encoding.UTF8.GetBytes($$"""
                <h1 data-automation-id='pdp-productInformation-title'>Headphones</h1>
                <div data-automation-id='pdp-buyBox-desktop'><div><div class='PricingUI-module__container'><p class='UI__pricingLabelMain'>{{desktop}}</p></div></div></div>
                <div data-automation-id='pdp-buyBox-mobile'><div><div class='PricingUI-module__container'><p class='UI__pricingLabelMain'>{{mobile}}</p></div></div></div>
                """),
            MediaType = "text/html"
        };

        // Act
        var result = _extractor.Extract(document);

        // Assert
        Assert.Null(result.Price);
        Assert.Equal(
            unsupported,
            result.CurrencyUnsupported);
    }

    [Fact]
    public void Extract_WhenAkamaiChallengeReturnsSuccess_DoesNotSuggestChallengeLogo()
    {
        // Arrange
        var document = CreateDocument("""
            <title>Access verification</title><div id='sec-if-cpt-container'></div>
            <meta property='og:image' content='/akamai-logo.svg'>
            """);

        // Act
        var result = _extractor.Extract(document);

        // Assert
        Assert.Null(result.Name);
        Assert.Null(result.ImageUrl);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\t")]
    [InlineData("\u0001")]
    public void Extract_WhenJsonLdDescriptionContainsRawControls_PreservesProductFields(string control)
    {
        // Arrange
        var document = CreateDocument($$$"""
            <script type="application/ld+json">
            {"@type":"Product","name":"Gift","description":"An escaped \"quote\" and \\ slash{{{control}}}next line",
            "image":"/cover.jpg","offers":{"price":"14.95","priceCurrency":"EUR"}}
            </script>
            """);

        // Act
        var result = _extractor.Extract(document);

        // Assert
        Assert.Equal(
            "Gift",
            result.Name);
        Assert.Equal(
            14.95m,
            result.Price);
        Assert.Equal(
            "https://example.com/cover.jpg",
            result.ImageUrl?.AbsoluteUri);
    }

    [Theory]
    [InlineData("\"url\":\"https://example.com/product\"")]
    [InlineData("\"offers\":{\"url\":\"/product\",\"price\":\"14.95\",\"priceCurrency\":\"EUR\"}")]
    [InlineData("\"offers\":[{\"url\":\"/product\",\"price\":\"14.95\",\"priceCurrency\":\"EUR\"}]")]
    public void Extract_WhenOneProductExplicitlyIdentifiesPage_IgnoresReviewsAndRecommendations(string identity)
    {
        // Arrange
        var document = CreateDocument($$$"""
            <script type="application/ld+json">
            [{"@type":"Product","name":"Main product",{{{identity}}}},
             {"@type":"Product","name":"Main product","review":{"@type":"Review"}},
             {"@type":"Product","name":"Recommendation","url":"/different"}]
            </script>
            """);

        // Act
        var result = _extractor.Extract(document);

        // Assert
        Assert.Equal(
            "Main product",
            result.Name);
    }

    [Theory]
    [InlineData("/different")]
    [InlineData("https://other.example/product")]
    [InlineData("http://[invalid")]
    [InlineData("/product?sku=different")]
    [InlineData("")]
    [InlineData(" ")]
    public void Extract_WhenProductUrlsDoNotIdentifyPage_RefusesAmbiguousProducts(string identity)
    {
        // Arrange
        var document = CreateDocument($$"""
            <script type="application/ld+json">
            [{"@type":"Product","name":"One","url":"{{identity}}"},
             {"@type":"Product","name":"Two"}]
            </script>
            """);

        // Act
        var result = _extractor.Extract(document);

        // Assert
        Assert.Null(result.Name);
        Assert.Null(result.Price);
        Assert.Null(result.ImageUrl);
    }

    [Fact]
    public void Extract_WhenMultipleOffersIdentifyPage_RefusesToChooseVariant()
    {
        // Arrange
        var document = CreateDocument("""
            <script type="application/ld+json">
            [{"@type":"Product","name":"One","url":"/product"},
             {"@type":"Product","name":"Two","url":"/product"}]
            </script>
            """);

        // Act
        var result = _extractor.Extract(document);

        // Assert
        Assert.Null(result.Name);
    }

    [Theory]
    [InlineData("<img alt='media/cover' src='https://cdn.example.com/images/cover'>", "https://cdn.example.com/images/cover")]
    [InlineData("<img alt='media/cover' src='/images/cover'>", "https://example.com/images/cover")]
    [InlineData("<img alt='other' src='/logo'>", "https://example.com/media/cover")]
    [InlineData("<img alt='media/cover' src='http://[invalid'>", "https://example.com/media/cover")]
    [InlineData("<img alt='media/cover' src='data:image/png;base64,AA'>", "https://example.com/media/cover")]
    [InlineData("<img alt='media/cover' src='/one'><img alt='media/cover' src='/two'>", "https://example.com/media/cover")]
    [InlineData("<img alt='media/cover' src='/one'><img alt='media/cover' src='/one'>", "https://example.com/one")]
    public void Extract_WhenRelativeImageHasVisibleEquivalent_UsesOnlyExactUnambiguousMatch(
        string markup,
        string expected)
    {
        // Arrange
        var document = CreateDocument($$"""
            <script type="application/ld+json">{"@type":"Product","name":"Gift","image":"media/cover"}</script>
            {{markup}}
            """);

        // Act
        var result = _extractor.Extract(document);

        // Assert
        Assert.Equal(
            expected,
            result.ImageUrl?.AbsoluteUri);
    }

    [Theory]
    [InlineData("<form id='challenge-form'></form>")]
    [InlineData("<div id='cf-challenge-running'></div>")]
    [InlineData("<div id='px-captcha'></div>")]
    [InlineData("<script src='/cdn-cgi/challenge-platform/scripts/jsd/main.js'></script>")]
    [InlineData("<script>var scriptUrl = '/cdn-cgi/challenge-platform/scripts/jsd/main.js';</script>")]
    public void Extract_WhenBrowserChallengeReplacesProduct_DoesNotImportSiteTitleOrLogo(string challenge)
    {
        // Arrange
        var document = CreateDocument($$"""
            <title>Store</title><meta property="og:title" content="Store.com"><meta property="og:image" content="/logo.png">
            {{challenge}}
            """);

        // Act
        var result = _extractor.Extract(document);

        // Assert
        Assert.Null(result.Name);
        Assert.Null(result.Price);
        Assert.Null(result.ImageUrl);
    }

    [Fact]
    public void Extract_WhenProductPageAlsoContainsChallengeLibrary_KeepsGenuineProduct()
    {
        // Arrange
        var document = CreateDocument("""
            <script src='/cdn-cgi/challenge-platform/scripts/jsd/main.js'></script>
            <script type='application/ld+json'>{"@type":"Product","name":"Gift"}</script>
            """);

        // Act
        var result = _extractor.Extract(document);

        // Assert
        Assert.Equal(
            "Gift",
            result.Name);
    }

    [Theory]
    [InlineData("https://www.amazon.fr/dp/B0EXAMPLE01?ref=sharing")]
    [InlineData("https://amazon.fr/Product-name/dp/2755681500")]
    [InlineData("https://m.amazon.fr/gp/product/B0EXAMPLE02")]
    [InlineData("https://www.amazon.de/dp/B0EXAMPLE03")]
    [InlineData("https://www.amazon.co.uk/gp/aw/d/B0EXAMPLE04")]
    [InlineData("https://www.amazon.com/dp/B0EXAMPLE05")]
    [InlineData("https://www.amazon.com.be/dp/B0EXAMPLE06")]
    [InlineData("https://www.amazon.co.jp/dp/B0EXAMPLE07")]
    [InlineData("https://www.amazon.com.au/dp/B0EXAMPLE08")]
    public void Extract_WhenAmazonProductUsesAnyProductPath_ReadsMainProduct(string url)
    {
        // Arrange
        var document = new ImportDocument
        {
            Url = new Uri(url),
            Content = Encoding.UTF8.GetBytes("""
                <title>Amazon.fr</title>
                <span id="productTitle">  A book &amp; a gift
                  for everyone  </span>
                <img id="landingImage" data-old-hires="https://images.example.com/main.jpg" src="/small.jpg">
                <div id="corePriceDisplay_desktop_feature_div">
                  <span class="a-price a-text-price"><span class="a-offscreen">25,00 €</span></span>
                  <span class="priceToPay"><span class="a-offscreen">19,90 €</span><span aria-hidden="true">19,90 €</span></span>
                </div>
                <div id="recommendations"><span class="priceToPay"><span class="a-offscreen">99,00 €</span></span></div>
                <script type="application/ld+json">[{"@type":"Product","name":"Other one"},{"@type":"Product","name":"Other two"}]</script>
                """)
        };

        // Act
        var result = _extractor.Extract(document);

        // Assert
        Assert.Equal(
            "A book & a gift for everyone",
            result.Name);
        Assert.Equal(
            "https://images.example.com/main.jpg",
            result.ImageUrl?.AbsoluteUri);
        Assert.Equal(
            19.90m,
            result.Price);
        Assert.False(result.CurrencyUnsupported);
    }

    [Theory]
    [InlineData("https://amazon.fr.example.com/product")]
    [InlineData("https://notamazon.fr/product")]
    [InlineData("https://amazon-fr.example.com/product")]
    public void Extract_WhenHostOnlyResemblesAmazon_KeepsGenericExtraction(string url)
    {
        // Arrange
        var document = new ImportDocument
        {
            Url = new Uri(url),
            Content = Encoding.UTF8.GetBytes("<title>Generic product</title><span id='productTitle'>Not selected</span>")
        };

        // Act
        var result = _extractor.Extract(document);

        // Assert
        Assert.Equal(
            "Generic product",
            result.Name);
    }

    [Theory]
    [InlineData("<title>Amazon.fr</title>")]
    [InlineData("<title>Amazon.com</title><meta property='og:image' content='/logo.jpg'>")]
    [InlineData("<title>Robot Check</title><form action='/errors/validateCaptcha'><input id='captchacharacters'></form>")]
    [InlineData("<title>Continue shopping</title><script type='application/ld+json'>{\"@type\":\"Product\",\"name\":\"Untrusted\"}</script>")]
    [InlineData("<span id='productTitle'> </span>")]
    [InlineData("<span id='productTitle'>One</span><span id='ebooksProductTitle'>Two</span>")]
    [InlineData("<span id='productTitle'>Book</span><form action='/errors/validateCaptcha'></form>")]
    [InlineData("<span id='productTitle'>Book</span><input id='captchacharacters'>")]
    public void Extract_WhenAmazonPageIsNotAnUnambiguousProduct_ReturnsNoSuggestions(string html)
    {
        // Arrange
        var document = CreateAmazonDocument(html);

        // Act
        var result = _extractor.Extract(document);

        // Assert
        Assert.Null(result.Name);
        Assert.Null(result.Price);
        Assert.Null(result.ImageUrl);
        Assert.False(result.CurrencyUnsupported);
    }

    [Theory]
    [InlineData("landingImage", "data-old-hires='/large.jpg' src='/small.jpg'", "https://www.amazon.fr/large.jpg")]
    [InlineData("imgBlkFront", "data-a-hires='/large.jpg' src='/small.jpg'", "https://www.amazon.fr/large.jpg")]
    [InlineData("ebooksImgBlkFront", "src='//images.example.com/cover.jpg'", "https://images.example.com/cover.jpg")]
    [InlineData("landingImage", "data-old-hires=' ' src='/small.jpg'", "https://www.amazon.fr/small.jpg")]
    [InlineData("landingImage", "data-old-hires='http://[invalid' src='/small.jpg'", "https://www.amazon.fr/small.jpg")]
    [InlineData("landingImage", "data-old-hires='data:image/gif;base64,AAAA' src='/small.jpg'", "https://www.amazon.fr/small.jpg")]
    [InlineData("landingImage", "src='http://images.example.com/cover.jpg'", "http://images.example.com/cover.jpg")]
    [InlineData("landingImage", "src='javascript:alert(1)'", null)]
    [InlineData("landingImage", "src='data:image/gif;base64,AAAA'", null)]
    [InlineData("landingImage", "", null)]
    [InlineData("unrelatedImage", "src='/logo.jpg'", null)]
    public void Extract_WhenAmazonMainImageUsesSupportedMarkup_ReturnsOneHttpCandidate(
        string id,
        string attributes,
        string? expected)
    {
        // Arrange
        var document = CreateAmazonDocument($"<span id='productTitle'>Book</span><img id='{id}' {attributes}>");

        // Act
        var result = _extractor.Extract(document);

        // Assert
        Assert.Equal(
            expected,
            result.ImageUrl?.AbsoluteUri);
        Assert.Equal(
            "Book",
            result.Name);
    }

    [Theory]
    [InlineData("9,90 €", "9.90", false)]
    [InlineData("9,90\u00a0€", "9.90", false)]
    [InlineData("EUR 9.90", "9.90", false)]
    [InlineData("€9,9", "9.9", false)]
    [InlineData("1 299,95 €", "1299.95", false)]
    [InlineData("1\u202f299,95 €", "1299.95", false)]
    [InlineData("1.299,95 €", "1299.95", false)]
    [InlineData("1,299.95 EUR", "1299.95", false)]
    [InlineData("99 999 999,99 €", "99999999.99", false)]
    [InlineData("9 €", "9", false)]
    [InlineData("0 €", null, false)]
    [InlineData("-9,90 €", null, false)]
    [InlineData("100000000 €", null, false)]
    [InlineData("12,345 €", null, false)]
    [InlineData("9,90–19,90 €", null, false)]
    [InlineData("NaN €", null, false)]
    [InlineData("9,90 €/mois", null, false)]
    [InlineData("£9.90", null, true)]
    [InlineData("$9.90", null, true)]
    [InlineData("9,90", null, true)]
    [InlineData(" ", null, false)]
    public void Extract_WhenAmazonPriceIsLocalized_RequiresAnExactEuroAmount(
        string text,
        string? expected,
        bool unsupported)
    {
        // Arrange
        var document = CreateAmazonDocument($"""
            <span id="productTitle">Book</span>
            <div id="corePriceDisplay_desktop_feature_div"><span class="priceToPay"><span class="a-offscreen">{text}</span></span></div>
            """);

        // Act
        var result = _extractor.Extract(document);

        // Assert
        Assert.Equal(
            expected,
            result.Price?.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(
            unsupported,
            result.CurrencyUnsupported);
    }

    [Theory]
    [InlineData("corePriceDisplay_desktop_feature_div")]
    [InlineData("corePriceDisplay_mobile_feature_div")]
    [InlineData("corePrice_desktop")]
    [InlineData("corePrice_feature_div")]
    [InlineData("corePrice_mobile_feature_div")]
    [InlineData("apex_desktop")]
    [InlineData("apex_mobile")]
    [InlineData("buybox")]
    [InlineData("mobile_buybox")]
    public void Extract_WhenAmazonPriceUsesPrimaryOfferContainer_ReadsOnlyThatOffer(string id)
    {
        // Arrange
        var document = CreateAmazonDocument($"""
            <span id="ebooksProductTitle">Digital book</span>
            <div id="{id}"><span id="apex-pricetopay-accessibility-label">9,90 €</span></div>
            <div class="recommendations"><span class="priceToPay">49,90 €</span></div>
            """);

        // Act
        var result = _extractor.Extract(document);

        // Assert
        Assert.Equal(
            9.90m,
            result.Price);
        Assert.Equal(
            "Digital book",
            result.Name);
    }

    [Theory]
    [InlineData("priceblock_ourprice")]
    [InlineData("priceblock_dealprice")]
    [InlineData("priceblock_saleprice")]
    public void Extract_WhenAmazonUsesLegacyOfferMarkup_ReadsExplicitEuroPrice(string id)
    {
        // Arrange
        var document = CreateAmazonDocument($"<span id='productTitle'>Book</span><span id='{id}'>9,90 €</span>");

        // Act
        var result = _extractor.Extract(document);

        // Assert
        Assert.Equal(
            9.90m,
            result.Price);
    }

    [Fact]
    public void Extract_WhenAmazonPriceHasEmptyAccessibleText_ReadsExplicitPriceParts()
    {
        // Arrange
        var document = CreateAmazonDocument("""
            <span id="productTitle">Book</span>
            <div id="corePriceDisplay_desktop_feature_div">
              <span id="apex-pricetopay-accessibility-label">9,90 €</span>
              <span class="priceToPay"><span class="a-offscreen"> </span><span aria-hidden="true">
                <span class="a-price-whole">9<span class="a-price-decimal">,</span></span>
                <span class="a-price-fraction">90</span>
                <span class="a-price-symbol">€</span>
              </span></span>
            </div>
            """);

        // Act
        var result = _extractor.Extract(document);

        // Assert
        Assert.Equal(
            9.90m,
            result.Price);
    }

    [Theory]
    [InlineData("9,90 €", "10,90 €")]
    [InlineData("9,90 €", "$9.90")]
    [InlineData("9,90 €", "9,90–19,90 €")]
    public void Extract_WhenAmazonPrimaryPricesConflict_DoesNotChooseOrUseLegacyPrice(
        string first,
        string second)
    {
        // Arrange
        var document = CreateAmazonDocument($"""
            <span id="productTitle">Book</span>
            <div id="corePriceDisplay_desktop_feature_div"><span class="priceToPay">{first}</span></div>
            <div id="apex_mobile"><span class="priceToPay">{second}</span></div>
            <span id="priceblock_ourprice">1,00 €</span>
            """);

        // Act
        var result = _extractor.Extract(document);

        // Assert
        Assert.Null(result.Price);
    }

    [Fact]
    public void Extract_WhenAmazonNameExceedsTheCreationLimit_DoesNotTruncateIt()
    {
        // Arrange
        var document = CreateAmazonDocument($"<span id='productTitle'>{new string('x', 101)}</span>");

        // Act
        var result = _extractor.Extract(document);

        // Assert
        Assert.Null(result.Name);
    }

    private static ImportDocument CreateAmazonDocument(string html)
    {

        return new ImportDocument
        {
            Url = new Uri("https://www.amazon.fr/dp/B0EXAMPLE01"),
            Content = Encoding.UTF8.GetBytes(html),
            MediaType = "text/html"
        };
    }
    [Fact]
    public void Extract_WhenJsonLdProductIsComplete_PrefersStructuredFields()
    {
        // Arrange
        var document = CreateDocument("""
            <title>Fallback</title><meta property="og:title" content="Other">
            <script type="application/ld+json">
            {"@context":"https://private.invalid/context","@type":"Product","name":" Gift ",
             "image":"/product.png","offers":{"price":"12.34","priceCurrency":"EUR"}}
            </script>
        """);

        // Act
        var result = _extractor.Extract(document);

        // Assert
        Assert.Equal(
            "Gift",
            result.Name);
        Assert.Equal(
            12.34m,
            result.Price);
        Assert.Equal(
            "https://example.com/product.png",
            result.ImageUrl?.AbsoluteUri);
        Assert.False(result.CurrencyUnsupported);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("<title> Gift &amp; book </title>", "Gift & book")]
    [InlineData("<meta property='og:title' content='Gift'><title>Fallback</title>", "Gift")]
    [InlineData("<meta name='OG:TITLE' content='Gift'>", "Gift")]
    [InlineData("<meta property='og:title' content=' '><title>Fallback</title>", "Fallback")]
    [InlineData("<meta property='og:title'><title>Fallback</title>", "Fallback")]
    [InlineData("<meta property='og:title' content='One'><meta property='og:title' content='Two'>", null)]
    [InlineData("<script type='application/ld+json'>{invalid</script><title>Fallback</title>", "Fallback")]
    [InlineData("<script type='application/ld+json'>null</script>", null)]
    [InlineData("<script type='application/ld+json'>{}</script>", null)]
    [InlineData("<script type='application/ld+json'>{\"@type\":null}</script>", null)]
    [InlineData("<script type='application/ld+json'>{\"@type\":\"Thing\"}</script>", null)]
    [InlineData("<script type='application/ld+json'>{\"@graph\":[{\"@type\":[\"Thing\",\"Product\"],\"name\":\"Gift\"}]}</script>", "Gift")]
    [InlineData("<script type='application/ld+json'>[{\"@type\":\"https://schema.org/Product\",\"name\":\"Gift\"}]</script>", "Gift")]
    [InlineData("<script type='application/ld+json'>{\"@type\":\"http://schema.org/Product\",\"name\":\"Gift\"}</script>", "Gift")]
    [InlineData("<script type='application/ld+json'>[{\"@type\":\"Product\"},{\"@type\":\"Product\"}]</script><title>Ambiguous</title>", null)]
    public void Extract_WhenMetadataIsIncomplete_ReturnsOnlyUsableName(
        string html,
        string? name)
    {
        // Arrange
        var document = CreateDocument(html);

        // Act
        var result = _extractor.Extract(document);

        // Assert
        Assert.Equal(
            name,
            result.Name);
        Assert.Null(result.Price);
        Assert.Null(result.ImageUrl);
    }

    [Theory]
    [InlineData("\"12.34\"", "\"EUR\"", "12.34", false)]
    [InlineData("12.34", "\"eur\"", "12.34", false)]
    [InlineData("\"12.34\"", "\"USD\"", null, true)]
    [InlineData("\"12.34\"", "null", null, true)]
    [InlineData("\"12.345\"", "\"EUR\"", null, false)]
    [InlineData("\"100000000\"", "\"EUR\"", null, false)]
    [InlineData("\"0\"", "\"EUR\"", null, false)]
    [InlineData("\"-1\"", "\"EUR\"", null, false)]
    [InlineData("\"NaN\"", "\"EUR\"", null, false)]
    [InlineData("null", "\"EUR\"", null, false)]
    [InlineData("{}", "\"EUR\"", null, false)]
    public void Extract_WhenPriceIsChecked_DoesNotGuessOrConvert(
        string amount,
        string currency,
        string? expectedPrice,
        bool unsupported)
    {
        // Arrange
        var document = CreateDocument($$$"""
            <script type="application/ld+json">
            {"@type":"Product","offers":{"price":{{{amount}}},"priceCurrency":{{{currency}}}}}
            </script>
        """);

        // Act
        var result = _extractor.Extract(document);

        // Assert
        Assert.Equal(
            expectedPrice,
            result.Price?.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(
            unsupported,
            result.CurrencyUnsupported);
    }

    [Theory]
    [InlineData("[]", null)]
    [InlineData("[\"/first.png\",\"/second.png\"]", "https://example.com/first.png")]
    [InlineData("{\"url\":\"/image.png\"}", "https://example.com/image.png")]
    [InlineData("\"https://cdn.example.com/cover.jpg\"", "https://cdn.example.com/cover.jpg")]
    [InlineData("null", null)]
    [InlineData("\"http://[invalid\"", null)]
    public void Extract_WhenImageHasDifferentShapes_UsesAtMostOneCandidate(
        string image,
        string? expected)
    {
        // Arrange
        var document = CreateDocument($$"""
            <script type="application/ld+json">{"@type":"Product","image":{{image}}}</script>
        """);

        // Act
        var result = _extractor.Extract(document);

        // Assert
        Assert.Equal(
            expected,
            result.ImageUrl?.AbsoluteUri);
    }

    [Theory]
    [InlineData("utf-8")]
    [InlineData("\"utf-8\"")]
    [InlineData("unknown-encoding")]
    [InlineData("")]
    public void Extract_WhenCharsetIsSpecified_UsesSafeEncodingFallback(string charset)
    {
        // Arrange
        var document = CreateDocument(
            "<title>Gift</title>",
            charset);

        // Act
        var result = _extractor.Extract(document);

        // Assert
        Assert.Equal(
            "Gift",
            result.Name);
    }

    [Fact]
    public void Extract_WhenPricesDifferAcrossOffers_LeavesPriceEmpty()
    {
        // Arrange
        var document = CreateDocument("""
            <script type="application/ld+json">
            {"@type":"Product","offers":[{"price":1,"priceCurrency":"EUR"},{"price":2,"priceCurrency":"EUR"}]}
            </script>
        """);

        // Act
        var result = _extractor.Extract(document);

        // Assert
        Assert.Null(result.Price);
    }

    [Fact]
    public void Extract_WhenOpenGraphIsComplete_UsesMetadataWithoutFetchingResources()
    {
        // Arrange
        var document = CreateDocument("""
            <script>throw new Error("must not execute");</script>
            <meta property="og:title" content="Gift">
            <meta property="og:image" content="//images.example.com/image.png">
            <meta property="product:price:amount" content="42.50">
            <meta property="product:price:currency" content="EUR">
        """);

        // Act
        var result = _extractor.Extract(document);

        // Assert
        Assert.Equal(
            42.50m,
            result.Price);
        Assert.Equal(
            "https://images.example.com/image.png",
            result.ImageUrl?.AbsoluteUri);
    }

    [Theory]
    [InlineData("{}", "")]
    [InlineData("{\"price\":null}", "   ")]
    [InlineData("{\"price\":\" \"}", "")]
    public void Extract_WhenJsonLdFieldsAreEmpty_UsesOpenGraph(
        string offers,
        string image)
    {
        // Arrange
        var document = CreateDocument($$"""
            <script type="application/ld+json">
            {"@type":"Product","image":"{{image}}","offers":{{offers}}}
            </script>
            <meta property="og:image" content="/fallback.png">
            <meta property="product:price:amount" content="42.50">
            <meta property="product:price:currency" content="EUR">
        """);

        // Act
        var result = _extractor.Extract(document);

        // Assert
        Assert.Equal(
            42.50m,
            result.Price);
        Assert.Equal(
            "https://example.com/fallback.png",
            result.ImageUrl?.AbsoluteUri);
    }

    [Theory]
    [InlineData("{\"price\":12,\"priceCurrency\":\"USD\"}")]
    [InlineData("[{\"price\":12,\"priceCurrency\":\"EUR\"},{\"price\":13,\"priceCurrency\":\"EUR\"}]")]
    public void Extract_WhenJsonLdPriceCannotBeChosen_DoesNotOverrideWithOpenGraph(string offers)
    {
        // Arrange
        var document = CreateDocument($$"""
            <script type="application/ld+json">{"@type":"Product","offers":{{offers}}}</script>
            <meta property="product:price:amount" content="42.50">
            <meta property="product:price:currency" content="EUR">
        """);

        // Act
        var result = _extractor.Extract(document);

        // Assert
        Assert.Null(result.Price);
    }

    private static ImportDocument CreateDocument(
        string html,
        string? charset = null)
    {

        return new ImportDocument
        {
            Url = new Uri("https://example.com/product"),
            Content = Encoding.UTF8.GetBytes(html),
            Charset = charset,
            MediaType = "text/html"
        };
    }
}
