---
title: Deploy a Trax app
description: "Deploy a Trax app with trax deploy: a compose stack for any host, or an AWS environment, built from what each host reports it needs."
type: how-to
area: tooling
nav_order: 6
---

# Deploy a Trax app

`trax deploy` deploys the Trax hosts in a solution: as a compose stack for any machine that runs docker or podman,
or as an AWS environment. You name the projects and the choices only you can make in `trax.deploy.json`; each host
reports the rest itself when trax runs it once with `--trax-export-deploy`: its database, whether it serves the API
or runs the scheduler, its endpoints and its sign-in. The [CLI reference](/docs/reference/cli#deployments-trax-deploy)
has every option.

Before you start, the hosts end with `await app.RunTraxAsync(args);` in place of `app.Run()`, as the templates do,
and keep their runs in Postgres when `ConnectionStrings:Trax` is set
([Switch a template to Postgres](/docs/reference/switch-a-template-to-postgres)). An API host maps a health check
(`app.MapHealthChecks("/trax/health")`).

## 1. Describe the deployment

```bash
trax deploy init --app orders            # compose only
trax deploy init --app orders --target aws --region us-west-2 --domain orders.example.com
```

`init` makes a service of every Trax host project under the directory. List the configuration the hosts read
under `settings` (an issuer, an origin) and `secrets` (an API key, a password), as environment names with `__` for
`:`. A deploy refuses to go ahead while one has no value.

## 2. Run it on one machine

```bash
trax deploy --target compose --build
cp .env.example .env                      # fill in every value; never commit it
docker compose up -d                      # or: podman compose up -d
```

The stack is Postgres, RabbitMQ when a host uses it, one `migrate` run of the new version, then each service. The
API service answers on port 8080.

## 3. Deploy to AWS

Sign in to the account the usual way (`aws sso login`, a profile, or a role in CI), then:

```bash
trax deploy plan --env production        # read .trax/deploy/aws/orders-production.template.json and the cost estimate
trax deploy secret set Stripe__ApiKey --env production
trax deploy --env production
trax deploy status --env production
```

The first deploy takes 15 to 25 minutes, most of it the database and, in production, the certificate. Later
deploys push new images, migrate, and roll the services. With `"auth": { "kind": "cognito" }`, the user pool trax
creates admits only users you invite; add the first in the Cognito console, and put operators in its `Operator`
group.

Use the `try` profile for an environment you will throw away: no domain, no NAT gateway, a small database deleted
with the stack. CloudFront serves it over HTTPS, but reaches the load balancer over plain HTTP inside AWS.

## One hub

The API and the scheduler run as one copy each. A deploy starts the new copy before it stops the old one, but runs
still live on the old copy when it stops can fail: a restarting host fails the runs it finds in progress, including
another host's. Workers scale freely (`count`). Running more than one hub waits on hosts holding leases on their
runs.

## Take it down

```bash
docker compose down                       # compose; add -v to drop the database too
trax deploy destroy --env production      # AWS; production's database leaves a final snapshot
```

## SDK Reference

> [TraxMigrate](/docs/sdk-reference/configuration/trax-migrate) | [DeployManifest](/docs/sdk-reference/configuration/deploy-manifest) | [UsePostgres](/docs/sdk-reference/configuration/use-postgres)
