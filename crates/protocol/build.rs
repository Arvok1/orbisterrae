use prost::Message;

fn main() -> Result<(), Box<dyn std::error::Error>> {
    println!("cargo:rerun-if-changed=../../packages/protocol/orbis.proto");
    println!("cargo:rerun-if-changed=../../packages/protocol/orbis.binpb");
    let bytes = std::fs::read("../../packages/protocol/orbis.binpb")?;
    let descriptors = prost_types::FileDescriptorSet::decode(bytes.as_slice())?;
    prost_build::Config::new().compile_fds(descriptors)?;
    Ok(())
}
