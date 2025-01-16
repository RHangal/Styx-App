import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { AuthService } from '@auth0/auth0-angular';
import { Observable } from 'rxjs';
import { switchMap } from 'rxjs/operators';
import { environment } from '../../environment';

@Injectable({
  providedIn: 'root',
})
export class PostsService {
  private baseUrl = environment.apiUrl;

  constructor(
    private http: HttpClient,
    private auth: AuthService // For token retrieval via getAccessTokenSilently()
  ) {}

  /**
   * Fetch posts by type (no user ID in path).
   * e.g. GET /posts/{postType}
   */
  getPostsByType(postType: string): Observable<any> {
    // If your endpoint doesn't need a token, call directly:
    return this.http.get<any[]>(`${this.baseUrl}posts/${postType}`);
  }

  /**
   * Create a post. The backend identifies the user from token 'sub'.
   * We no longer include 'Auth0UserId' in the body.
   */
  createPost(
    PostType: string,
    Name: string,
    Email: string,
    Caption: string
  ): Observable<any> {
    const body = { PostType, Name, Email, Caption };
    return this.auth.getAccessTokenSilently().pipe(
      switchMap((token: string) => {
        const headers = new HttpHeaders()
          .set('Authorization', `Bearer ${token}`)
          .set('Content-Type', 'application/json');

        return this.http.post(`${this.baseUrl}posts`, body, { headers });
      })
    );
  }

  /**
   * Upload media (file). The backend reads user from token, not from form data.
   */
  uploadMedia(file: File): Observable<any> {
    return this.auth.getAccessTokenSilently().pipe(
      switchMap((token: string) => {
        const formData = new FormData();
        formData.append('file', file);

        // If the backend wants a 'profile' or other field, add it:
        // formData.append('profile', 'pfp');

        const headers = new HttpHeaders().set(
          'Authorization',
          `Bearer ${token}`
        );
        return this.http.post(`${this.baseUrl}media/upload`, formData, {
          headers,
        });
      })
    );
  }

  /**
   * Update the post with a media link if your backend still requires a separate step.
   * The user is derived from the token, so 'postId' remains, but no 'auth0UserId'.
   */
  updatePostWithMedia(postId: string, mediaUrl: string): Observable<any> {
    const body = { mediaUrl };
    return this.auth.getAccessTokenSilently().pipe(
      switchMap((token: string) => {
        const headers = new HttpHeaders()
          .set('Authorization', `Bearer ${token}`)
          .set('Content-Type', 'application/json');

        return this.http.put(`${this.baseUrl}posts/media/${postId}`, body, {
          headers,
        });
      })
    );
  }

  /**
   * Reward daily coins for the user derived from the token (no 'auth0UserId' in body).
   */
  rewardDailyCoins(): Observable<any> {
    // The backend now identifies the user from the token's sub claim
    return this.auth.getAccessTokenSilently().pipe(
      switchMap((token: string) => {
        const headers = new HttpHeaders()
          .set('Authorization', `Bearer ${token}`)
          .set('Content-Type', 'application/json');

        // Possibly the backend no longer needs any body since user is in the token
        return this.http.post(
          `${this.baseUrl}users/reward-coins`,
          {},
          { headers }
        );
      })
    );
  }

  /**
   * Combined method to create a post, reward daily coins, and optionally upload media.
   */
  createPostWithOptionalMedia(
    PostType: string,
    Name: string,
    Email: string,
    Caption: string,
    MediaFile?: File
  ): Observable<any> {
    // We'll chain calls with nested switchMaps
    return this.createPost(PostType, Name, Email, Caption).pipe(
      switchMap((postResponse: any) => {
        const postId = postResponse.postId; // The newly created post's ID

        // 1) Reward daily coins
        return this.rewardDailyCoins().pipe(
          switchMap((rewardResponse: any) => {
            console.log('Daily coins rewarded:', rewardResponse);

            // 2) If media is provided, upload & update post
            if (MediaFile) {
              return this.uploadMedia(MediaFile).pipe(
                switchMap((mediaRes: any) => {
                  const mediaLink = mediaRes.url; // The returned file URL
                  return this.updatePostWithMedia(postId, mediaLink);
                })
              );
            } else {
              // No media, just return the rewardResponse or postResponse
              return new Observable((obs) => {
                obs.next({ rewardResponse, postResponse });
                obs.complete();
              });
            }
          })
        );
      })
    );
  }

  /**
   * Add comment to a post. The user is read from the token, so no 'auth0UserId'.
   */
  addComment(
    postId: string,
    text: string,
    commenterEmail: string,
    postEmail: string,
    name: string,
    commentId?: string
  ): Observable<any> {
    const body = {
      postId,
      text,
      commenterEmail,
      postEmail,
      name,
      commentId: commentId || '',
    };

    return this.auth.getAccessTokenSilently().pipe(
      switchMap((token: string) => {
        const headers = new HttpHeaders()
          .set('Authorization', `Bearer ${token}`)
          .set('Content-Type', 'application/json');

        return this.http.post(`${this.baseUrl}posts/comments`, body, {
          headers,
        });
      })
    );
  }

  /**
   * Toggle like on a post or comment. The user is derived from the token.
   */
  toggleLike(
    postId: string,
    commentId?: string,
    replyId?: string
  ): Observable<any> {
    const body = {
      postId,
      commentId: commentId || '',
      replyId: replyId || '',
    };

    return this.auth.getAccessTokenSilently().pipe(
      switchMap((token: string) => {
        const headers = new HttpHeaders()
          .set('Authorization', `Bearer ${token}`)
          .set('Content-Type', 'application/json');

        return this.http.put(`${this.baseUrl}posts/like`, body, { headers });
      })
    );
  }

  /**
   * Delete a post. The user is derived from the token.
   */
  deletePost(postId: string): Observable<any> {
    // The backend identifies the user from the token, so we pass an empty body if needed
    // or possibly just route param if your backend is updated.
    const options = {
      headers: new HttpHeaders({ 'Content-Type': 'application/json' }),
      // body: {} // If your backend needs an empty object or no body at all
    };

    return this.auth.getAccessTokenSilently().pipe(
      switchMap((token: string) => {
        options.headers = options.headers.set(
          'Authorization',
          `Bearer ${token}`
        );
        // If your backend supports "DELETE /posts/{postId}" with no body
        return this.http.delete(`${this.baseUrl}posts/${postId}`, options);
      })
    );
  }

  /**
   * Edit a comment. The user is derived from the token.
   */
  editComment(
    text: string,
    postId: string,
    commentId: string,
    replyId?: string
  ): Observable<any> {
    const body = {
      text,
      postId,
      commentId,
      replyId: replyId || undefined,
    };

    return this.auth.getAccessTokenSilently().pipe(
      switchMap((token: string) => {
        const headers = new HttpHeaders()
          .set('Authorization', `Bearer ${token}`)
          .set('Content-Type', 'application/json');

        return this.http.put(`${this.baseUrl}posts/comments`, body, {
          headers,
        });
      })
    );
  }

  /**
   * Delete a comment or reply. The user is identified from token's sub.
   */
  deleteComment(
    postId: string,
    commentId: string,
    replyId?: string
  ): Observable<any> {
    // Some backends let you do a DELETE with a body, or you might pass these as query params.
    const options = {
      headers: new HttpHeaders({ 'Content-Type': 'application/json' }),
      // body is typically the "postId", "commentId", "replyId".
      body: {
        postId,
        commentId,
        replyId: replyId || undefined,
      },
    };

    return this.auth.getAccessTokenSilently().pipe(
      switchMap((token: string) => {
        options.headers = options.headers.set(
          'Authorization',
          `Bearer ${token}`
        );
        return this.http.delete(`${this.baseUrl}posts/comments`, options);
      })
    );
  }
}
