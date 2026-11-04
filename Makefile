.PHONY: world
world:
	docker compose -f tools/world-pipeline/compose.yml run --build --rm world
