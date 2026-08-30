import { zodResolver } from "@hookform/resolvers/zod";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { Button } from "#/components/ui/button";
import { Input } from "#/components/ui/input";
import { Label } from "#/components/ui/label";
import {
	Select,
	SelectContent,
	SelectItem,
	SelectTrigger,
	SelectValue,
} from "#/components/ui/select";
import { Textarea } from "#/components/ui/textarea";
import { getApiErrorMessage } from "#/shared/lib/error-messages";
import { PlaylistScope, PlaylistVisibility } from "#/shared/types";
import { useCreatePlaylist } from "../hooks";

const NAME_MAX = 100;
const DESCRIPTION_MAX = 500;

const schema = z.object({
	name: z
		.string()
		.min(1, "Name is required")
		.max(NAME_MAX, `Name must be at most ${NAME_MAX} characters`),
	description: z
		.string()
		.max(
			DESCRIPTION_MAX,
			`Description must be at most ${DESCRIPTION_MAX} characters`,
		)
		.optional(),
	visibility: z.coerce.number(),
	scope: z.coerce.number(),
	channelId: z.string().optional(),
});

type FormValues = z.infer<typeof schema>;

type Props = {
	channelId?: string;
	onSuccess?: (playlistId: string) => void;
};

export function CreatePlaylistForm({ channelId, onSuccess }: Props) {
	const createPlaylist = useCreatePlaylist();

	const defaultScope = channelId ? PlaylistScope.Channel : PlaylistScope.User;

	const {
		register,
		handleSubmit,
		setValue,
		formState: { errors },
	} = useForm<FormValues>({
		resolver: zodResolver(schema),
		defaultValues: {
			visibility: PlaylistVisibility.Public,
			scope: defaultScope,
			channelId,
		},
	});

	function onSubmit(values: FormValues) {
		createPlaylist.mutate(
			{
				name: values.name,
				description: values.description?.trim() || null,
				visibility: values.visibility,
				scope: values.scope,
				channelId: values.channelId ?? null,
			},
			{ onSuccess: (data) => onSuccess?.(data.playlistId) },
		);
	}

	return (
		<form onSubmit={handleSubmit(onSubmit)} className="space-y-4">
			<div className="space-y-2">
				<Label htmlFor="playlist-name">Name</Label>
				<Input
					id="playlist-name"
					{...register("name")}
					placeholder="My Playlist"
				/>
				{errors.name && (
					<p className="text-sm text-destructive">{errors.name.message}</p>
				)}
			</div>

			<div className="space-y-2">
				<Label htmlFor="playlist-description">Description</Label>
				<Textarea
					id="playlist-description"
					{...register("description")}
					placeholder="What is this playlist about? (optional)"
					rows={3}
				/>
				{errors.description && (
					<p className="text-sm text-destructive">
						{errors.description.message}
					</p>
				)}
			</div>

			<div className="space-y-2">
				<Label>Visibility</Label>
				<Select
					defaultValue={String(PlaylistVisibility.Public)}
					onValueChange={(v) => setValue("visibility", Number(v))}
				>
					<SelectTrigger>
						<SelectValue />
					</SelectTrigger>
					<SelectContent>
						<SelectItem value={String(PlaylistVisibility.Public)}>
							Public
						</SelectItem>
						<SelectItem value={String(PlaylistVisibility.Private)}>
							Private
						</SelectItem>
					</SelectContent>
				</Select>
			</div>

			{createPlaylist.error && (
				<p className="text-sm text-destructive">
					{getApiErrorMessage(createPlaylist.error)}
				</p>
			)}

			<Button type="submit" disabled={createPlaylist.isPending}>
				{createPlaylist.isPending ? "Creating…" : "Create playlist"}
			</Button>
		</form>
	);
}
