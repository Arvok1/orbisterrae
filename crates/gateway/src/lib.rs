//! I/O adapters; gameplay decisions live in core/sim.
pub mod auth;
pub mod handoff;
pub mod world_service;

#[cfg(test)]
mod tests {
    #[test]
    fn hello() {
        assert_eq!(2 + 2, 4);
    }
}
