using Xunit;

namespace HQ.Plugins.Tests.LinkedIn;

/// <summary>
/// Groups every test class that reads or writes <see cref="HQ.Plugins.LinkedIn.LinkedInCommand"/>'s
/// process-wide statics (<c>LastConfig</c>, the (org, account)-keyed browser cache) into one xUnit
/// test collection with parallelization disabled.
///
/// Root cause this closes: xUnit runs distinct test classes in parallel by default (each class
/// with no explicit <c>[Collection]</c> gets its own implicit collection, and collections run
/// concurrently on separate threads). <c>LinkedInCommandTests.DoWork_NullOrganizationId_IsRefused_WithoutLaunchingABrowser</c>
/// and <c>DoWork_EmptyGuidOrganizationId_IsRefused</c> call the real (non-mocked) <c>DoWork</c>,
/// which unconditionally writes <c>LinkedInCommand.LastConfig = config</c> before the org
/// fail-closed check even runs (a documented, disclosed production side effect — see
/// <c>LastConfig</c>'s doc comment on the "known residual" ancillary-settings leak). Those tests'
/// <c>ServiceConfig.AccountLabel</c> is a synthetic value like "wp6a7-empty-org-&lt;hex&gt;". When
/// that write raced against <c>LinkedInTenancyTests.ResolveConfig_*</c> tests reading
/// <c>LastConfig</c> via <c>LinkedInLoginEndpoints.ResolveConfig</c> on another thread, the
/// tenancy tests observed the polluted config instead of the expected "default" AccountLabel and
/// failed intermittently in the full suite (they always passed when the LinkedIn folder, or
/// <c>LinkedInTenancyTests</c> alone, ran in isolation).
///
/// Putting both classes in this one, non-parallel collection removes the race; each class also
/// resets <see cref="HQ.Plugins.LinkedIn.LinkedInCommand.ResetForTests"/> in its constructor and
/// <c>Dispose</c> so no test depends on execution order within the collection either.
/// </summary>
[CollectionDefinition("LinkedIn command static state", DisableParallelization = true)]
public class LinkedInStaticStateCollection
{
}
