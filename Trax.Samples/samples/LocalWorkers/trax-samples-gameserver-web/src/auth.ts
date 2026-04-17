import NextAuth from "next-auth";
import Google from "next-auth/providers/google";

/**
 * NextAuth v5 configuration. Signs users in via Google, then captures the
 * Google-issued id-token so the frontend can forward it to the Trax API
 * as an Authorization: Bearer credential. The Trax API validates the token
 * against Google's JWKS (see Program.cs: AddTraxJwtAuth with the Google
 * authority).
 */
export const { handlers, signIn, signOut, auth } = NextAuth({
  providers: [Google],
  callbacks: {
    // Persist the Google id-token on the NextAuth session JWT so we can
    // read it from the client without a round trip.
    jwt({ token, account }) {
      if (account?.id_token) {
        token.idToken = account.id_token;
      }
      return token;
    },
    session({ session, token }) {
      if (token.idToken) {
        session.idToken = token.idToken as string;
      }
      return session;
    },
  },
});

declare module "next-auth" {
  interface Session {
    idToken?: string;
  }
}

declare module "@auth/core/jwt" {
  interface JWT {
    idToken?: string;
  }
}
