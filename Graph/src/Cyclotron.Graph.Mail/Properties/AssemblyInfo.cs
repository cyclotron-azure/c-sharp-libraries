using System.Runtime.CompilerServices;

// Cyclotron.Graph.Mail.Tests asserts directly on Mail's internal types (GraphMailClient,
// GraphMailOptionsValidator, GraphMessageMapping) rather than only through the public surface.
[assembly: InternalsVisibleTo("Cyclotron.Graph.Mail.Tests")]
