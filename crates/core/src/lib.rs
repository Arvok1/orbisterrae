//! Domain primitives: deterministic, without I/O.
pub mod time;
pub mod world;

#[cfg(test)]
mod tests {
    #[test]
    fn hello() {
        assert_eq!(2 + 2, 4);
    }
}
