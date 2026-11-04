.PHONY: world terrain
world:
	docker compose -f tools/world-pipeline/compose.yml run --build --rm world
	$(MAKE) terrain

terrain:
	docker compose -f tools/world-pipeline/compose.yml run --build --rm --entrypoint python world tools/world-pipeline/terrain.py
