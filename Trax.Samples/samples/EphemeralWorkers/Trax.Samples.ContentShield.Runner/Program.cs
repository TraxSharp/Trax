// Local development entry point: runs the Lambda function as a Kestrel web server, so `dotnet run`
// works. In production AWS Lambda invokes Function.FunctionHandler directly and this file is unused.
//
// The function reads its own configuration from appsettings.json next to the binary, but the
// Kestrel server RunLocalAsync starts reads appsettings.json from the content root, which is the
// current directory. Pointing the content root at the binary makes both read the same file, so the
// server listens on the URL appsettings.json names (5205) wherever `dotnet run` starts.

using Trax.Samples.ContentShield.Runner;

await new Function().RunLocalAsync([$"--contentRoot={AppContext.BaseDirectory}", .. args]);
