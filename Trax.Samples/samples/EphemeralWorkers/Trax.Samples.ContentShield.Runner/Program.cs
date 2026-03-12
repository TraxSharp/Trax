// Local development entry point.
// Runs the Lambda function as a Kestrel web server so you can use `dotnet run`.
// In production, AWS Lambda invokes Function.FunctionHandler directly.

using Trax.Samples.ContentShield.Runner;

await new Function().RunLocalAsync(args);
