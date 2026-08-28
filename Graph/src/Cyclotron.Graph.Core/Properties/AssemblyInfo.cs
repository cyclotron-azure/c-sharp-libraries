using System.Runtime.CompilerServices;

// Cyclotron.Graph.Mail's AddCyclotronGraphMail registers IGraphMailClient against the same base
// address, Prefer header, and GraphAuthHandler chain that AddCyclotronGraphCore configures for
// the Graph API named client. It reaches that configuration through
// GraphCoreServiceCollectionExtensions.AddCyclotronGraphApiHttpClient, an internal helper — hence
// this attribute.
[assembly: InternalsVisibleTo("Cyclotron.Graph.Mail")]

// Task 12 appends its own InternalsVisibleTo("Cyclotron.Graph.Core.Tests") attribute to this same
// file rather than creating a second AssemblyInfo.cs.

// Task 12 test project reaches Core's internal types (GraphAuthHandler, ClientSecretTokenProvider,
// GraphSubscriptionClient, GraphNotificationParser, GraphNotificationValidator,
// GraphCoreOptionsValidator) to assert on them directly.
[assembly: InternalsVisibleTo("Cyclotron.Graph.Core.Tests")]
