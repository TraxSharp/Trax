// What the website's copy of the deploy recordings says about AWS, which has not been recorded: there is no AWS
// account to record against yet. The home page's release picture tells the AWS story anyway, marked simulated: the
// Kubernetes recording's timing, with the steps below, which are what the ECS deploy does in the code. It carries no
// requests and no counts. deploy.mjs writes this into deploy-recordings.json under "simulated".
export const simulated = {
  aws: {
    simulated: true,
    reason: "Not recorded: there is no AWS account to record against yet.",
    modelledOn: { shape: "hub-workers", target: "kubernetes" },
    basis: [
      {
        step: "Before any service changes, runs the new image once as an ECS task with --trax-drain and waits for what is busy to finish.",
        source: "Trax.Cli/src/Trax.Cli/Deploy/Aws/AwsDeployer.cs (DrainAsync)",
      },
      {
        step: "Rolls each service with MinimumHealthyPercent 100 and MaximumPercent 200: the new task starts beside the old one, which stops once the new one is healthy.",
        source: "Trax.Cli/src/Trax.Cli/Deploy/Aws/AwsStack.cs (DeploymentConfiguration)",
      },
      {
        step: "Turns on the ECS deployment circuit breaker with rollback: a version that never gets healthy is rolled back.",
        source: "Trax.Cli/src/Trax.Cli/Deploy/Aws/AwsStack.cs (DeploymentCircuitBreaker)",
      },
      {
        step: "The load balancer's target group checks the hub's health endpoint and sends requests only to healthy tasks.",
        source: "Trax.Cli/src/Trax.Cli/Deploy/Aws/AwsStack.cs (HealthCheckPath)",
      },
    ],
  },
};
