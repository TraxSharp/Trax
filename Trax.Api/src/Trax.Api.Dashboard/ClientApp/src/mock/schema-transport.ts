import { execute, parse, type GraphQLSchema } from "graphql";
import type { TraxTransport } from "../trax/index.g";

/**
 * The end of the mock's layers: runs each document's text against an in-process executable schema
 * (the auto-mocked SDL). A stream never fires: the auto-mock schema cannot execute subscriptions, and
 * the stateful layer above answers the ones the mock drives.
 */
export function schemaTransport(schema: GraphQLSchema): TraxTransport {
  return {
    async execute(request) {
      const result = await execute({
        schema,
        document: parse(request.document.text),
        variableValues: { ...request.variables },
      });
      return {
        data: result.data ?? null,
        errors: result.errors?.map((e) => ({
          message: e.message,
          path: e.path,
          extensions: e.extensions as Record<string, unknown> | undefined,
        })),
      };
    },
    subscribe: () => () => {},
  };
}
