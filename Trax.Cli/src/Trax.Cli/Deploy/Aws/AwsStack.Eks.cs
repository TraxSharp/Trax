using System.Text.Json.Nodes;
using Trax.Core.Extensions;

namespace Trax.Cli.Deploy.Aws;

internal static partial class AwsStack
{
    /// <summary>The foundation output naming the role a kubernetes environment's pods assume through Pod Identity.</summary>
    public const string PodRoleOutput = "PodRoleArn";

    /// <summary>
    /// The foundation of a kubernetes environment on EKS: the same network, database and secrets an ECS runtime gets,
    /// and for the pods a role they assume through EKS Pod Identity, which may read only this environment's secrets,
    /// with one association per service account. The database admits the group the app's pods wear through Security
    /// Groups for Pods; <paramref name="nodeSecurityGroup"/>, given only for <c>"databaseAccess": "nodes"</c>, is
    /// admitted as well, and every pod on those nodes with it.
    /// </summary>
    public static JsonObject EksFoundation(
        DeployFile file,
        string environmentName,
        string ns,
        string? nodeSecurityGroup
    )
    {
        var env = file.Environment(environmentName);
        var aws = env.Eks.NotNull();
        var production = string.Equals(
            env.Profile,
            DeployEnvironment.Production,
            StringComparison.Ordinal
        );
        var foundation = new DeployEnvironment
        {
            Target = DeployEnvironment.Aws,
            Profile = env.Profile,
            Region = aws.Region,
            Network = aws.Network,
        };
        var template = Foundation(file, environmentName, foundation, production);
        var resources = template["Resources"].NotNull().AsObject();
        var outputs = template["Outputs"].NotNull().AsObject();
        var stack = file.StackName(environmentName);

        resources["PodRole"] = PodRole(file, stack);
        outputs[PodRoleOutput] = Output(GetAtt("PodRole", "Arn"));
        foreach (var service in file.Services.Keys)
            resources[$"{LogicalId(service)}PodIdentity"] = Resource(
                "AWS::EKS::PodIdentityAssociation",
                new JsonObject
                {
                    ["ClusterName"] = aws.Cluster.NotNull(),
                    ["Namespace"] = ns,
                    ["ServiceAccount"] = service,
                    ["RoleArn"] = GetAtt("PodRole", "Arn"),
                }
            );
        if (nodeSecurityGroup is not null)
            resources["DatabaseFromNodes"] = Resource(
                "AWS::EC2::SecurityGroupIngress",
                new JsonObject
                {
                    ["GroupId"] = Ref("DatabaseSecurityGroup"),
                    ["IpProtocol"] = "tcp",
                    ["FromPort"] = 5432,
                    ["ToPort"] = 5432,
                    ["SourceSecurityGroupId"] = nodeSecurityGroup,
                    ["Description"] = "Every pod on the cluster's nodes (databaseAccess: nodes)",
                }
            );
        return template;
    }

    /// <summary>
    /// The role the pods assume: trusted only by EKS Pod Identity, and allowed only to read the database's secret and
    /// this environment's app secrets.
    /// </summary>
    private static JsonObject PodRole(DeployFile file, string stack)
    {
        var secrets = new JsonArray(Ref("DatabaseSecret"));
        if (file.Secrets.Count > 0)
            secrets.Add(
                Sub(
                    $"arn:${{AWS::Partition}}:secretsmanager:${{AWS::Region}}:${{AWS::AccountId}}:secret:{stack}/app/*"
                )
            );
        return Resource(
            "AWS::IAM::Role",
            new JsonObject
            {
                ["AssumeRolePolicyDocument"] = new JsonObject
                {
                    ["Version"] = "2012-10-17",
                    ["Statement"] = new JsonArray(
                        new JsonObject
                        {
                            ["Effect"] = "Allow",
                            ["Principal"] = new JsonObject
                            {
                                ["Service"] = "pods.eks.amazonaws.com",
                            },
                            ["Action"] = new JsonArray("sts:AssumeRole", "sts:TagSession"),
                        }
                    ),
                },
                ["Policies"] = new JsonArray(
                    new JsonObject
                    {
                        ["PolicyName"] = "read-the-environments-secrets",
                        ["PolicyDocument"] = new JsonObject
                        {
                            ["Version"] = "2012-10-17",
                            ["Statement"] = new JsonArray(
                                new JsonObject
                                {
                                    ["Effect"] = "Allow",
                                    ["Action"] = new JsonArray("secretsmanager:GetSecretValue"),
                                    ["Resource"] = secrets,
                                }
                            ),
                        },
                    }
                ),
                ["Tags"] = NameTag($"{stack}-pods"),
            }
        );
    }
}
