//! Sistema de criação de itens únicos (docs/CRAFTING.md).
//! Sem I/O. Funções puras e determinísticas. RNG por seed derivada de (craft_id, step_id, exec_seed).

#[cfg(test)]
mod tests {
    #[test]
    fn hello() {
        assert_eq!(2 + 2, 4);
    }
}
