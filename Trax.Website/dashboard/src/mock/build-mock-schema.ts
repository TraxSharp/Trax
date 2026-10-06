import { addMocksToSchema } from "@graphql-tools/mock";
import { makeExecutableSchema } from "@graphql-tools/schema";
import type { GraphQLSchema } from "graphql";
import { defaultMocks } from "./default-mocks";
import { SCHEMA_SDL } from "./schema-sdl";

type AddMocksOptions = Parameters<typeof addMocksToSchema>[0];

export interface MockSchemaOverrides {
  /** Per-type mock factories layered over the scalar baseline, keyed by GraphQL type name. */
  mocks?: AddMocksOptions["mocks"];
  /** Field resolvers for fully-controlled shapes (and __typename for unions/interfaces). */
  resolvers?: AddMocksOptions["resolvers"];
}

// Build a fresh executable schema per call. Caching a single base schema across clients let
// addMocksToSchema accumulate state on it, so a mutation in one story could leave the next
// story's reads returning nothing. Parsing ~12k chars of SDL is cheap enough to redo.
function getBaseSchema(): GraphQLSchema {
  return makeExecutableSchema({ typeDefs: SCHEMA_SDL });
}

/**
 * Build an in-process, auto-mocked executable schema from the exported Trax SDL. Every
 * field resolves to plausible fake data; pass `overrides` to pin the shapes a given
 * story/test cares about.
 */
export function buildMockSchema(overrides: MockSchemaOverrides = {}): GraphQLSchema {
  return addMocksToSchema({
    schema: getBaseSchema(),
    mocks: { ...defaultMocks, ...overrides.mocks },
    resolvers: overrides.resolvers,
    preserveResolvers: false,
  });
}
