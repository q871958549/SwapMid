using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Volo.Abp.AspNetCore.Mvc.UI.RazorPages;
using Volo.Abp.Localization;
using Volo.Abp.OpenIddict.Applications;

namespace SwapMid.Pages;

public class IndexModel : AbpPageModel
{
    public List<OpenIddictApplication>? Applications { get; protected set; }

    public IReadOnlyList<LanguageInfo>? Languages { get; protected set; }

    public string? CurrentLanguage { get; protected set; }

    public string FrontendUrl { get; protected set; } = "http://localhost:5175";

    protected IOpenIddictApplicationRepository OpenIdApplicationRepository { get; }

    protected ILanguageProvider LanguageProvider { get; }

    protected IConfiguration Configuration { get; }

    public IndexModel(
        IOpenIddictApplicationRepository openIdApplicationmRepository,
        ILanguageProvider languageProvider,
        IConfiguration configuration)
    {
        OpenIdApplicationRepository = openIdApplicationmRepository;
        LanguageProvider = languageProvider;
        Configuration = configuration;
    }

    public async Task OnGetAsync()
    {
        FrontendUrl = Configuration["App:FrontendUrl"] ?? FrontendUrl;
        Applications = new List<OpenIddictApplication>();

        Languages = await LanguageProvider.GetLanguagesAsync();
        CurrentLanguage = CultureInfo.CurrentCulture.DisplayName;
    }
}
