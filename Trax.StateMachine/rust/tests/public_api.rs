// The crate's public surface is a committed file, tests/public-api.txt, so a change to it shows in review as the C#
// packages' PublicApi baselines and the npm package's exports snapshot do.

#[path = "support/surface.rs"]
mod surface;

#[test]
fn the_public_surface_is_the_committed_one() {
    surface::check_snapshot(env!("CARGO_MANIFEST_DIR"), "tests/public-api.txt", "trax-state-machine");
}
